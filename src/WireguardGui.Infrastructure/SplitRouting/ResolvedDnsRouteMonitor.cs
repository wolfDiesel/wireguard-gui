using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class ResolvedDnsRouteMonitor(
    IPolicyRoutingSetup policyRoutingSetup,
    IProcessRunner processRunner,
    IAppDataPaths appDataPaths,
    ILogger<ResolvedDnsRouteMonitor> logger) : IResolvedDnsRouteMonitor, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _flusher;
    private VpnProfile? _profile;
    private IReadOnlyList<string> _suffixes = [];
    private int? _monitorPid;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _loop is { IsCompleted: false };
        }
    }

    public async Task StartAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var suffixes = DnsRouteSuffixes.FromSettings(profile.SplitRouting);
        if (!policyRoutingSetup.IsAvailable ||
            suffixes.Count == 0 ||
            !processRunner.IsCommandAvailable("resolvectl"))
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await StopAsync(cancellationToken).ConfigureAwait(false);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _profile = profile;
            _suffixes = suffixes;
            _cts = cts;
            _queued.Clear();
            _loop = Task.Run(() => RunMonitorAsync(cts.Token), CancellationToken.None);
            _flusher = Task.Run(() => RunFlusherAsync(cts.Token), CancellationToken.None);
        }

        logger.LogInformation(
            "Resolved DNS route monitor started for {Profile} ({SuffixCount} suffixes)",
            profile.Name,
            suffixes.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;
        Task? flusher;
        string? name;
        int? pid;

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            flusher = _flusher;
            name = _profile?.Name;
            pid = _monitorPid;
            _cts = null;
            _loop = null;
            _flusher = null;
            _profile = null;
            _suffixes = [];
            _monitorPid = null;
            _queued.Clear();
        }

        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch
        {
        }

        await StopPrivilegedMonitorAsync(pid, cancellationToken).ConfigureAwait(false);
        await WaitQuietAsync(loop, cancellationToken).ConfigureAwait(false);
        await WaitQuietAsync(flusher, cancellationToken).ConfigureAwait(false);
        cts.Dispose();

        if (name is not null)
            logger.LogInformation("Resolved DNS route monitor stopped for {Profile}", name);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _flushLock.Dispose();
    }

    private async Task RunMonitorAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested)
        {
            FileStream? fifo = null;
            try
            {
                Directory.CreateDirectory(appDataPaths.DataRoot);
                var fifoPath = FifoPath;
                await EnsureFifoAsync(fifoPath, cancellationToken).ConfigureAwait(false);

                // Open reader first so the privileged writer does not block forever.
                var openRead = Task.Run(
                    () => new FileStream(
                        fifoPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite),
                    cancellationToken);

                var start = await processRunner.RunPrivilegedShellAsync(
                        BuildStartMonitorScript(fifoPath),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!start.IsSuccess)
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(start.StandardError)
                            ? "Failed to start resolvectl monitor"
                            : start.StandardError.Trim());

                var pidLine = start.StandardOutput
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault();
                if (!int.TryParse(pidLine, out var pid) || pid <= 0)
                {
                    throw new InvalidOperationException(
                        "resolvectl monitor did not return a PID: " + start.StandardOutput.Trim());
                }

                lock (_gate)
                    _monitorPid = pid;

                using var openCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                openCts.CancelAfter(TimeSpan.FromSeconds(15));
                fifo = await openRead.WaitAsync(openCts.Token).ConfigureAwait(false);

                logger.LogInformation(
                    "resolvectl monitor attached (pid {Pid}, via existing privileged session)",
                    pid);

                using var reader = new StreamReader(fifo, Encoding.UTF8);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                        break;

                    if (!TryParseResolvectlMonitorLine(line, out var host, out var address))
                        continue;

                    await OnResolvedAsync(host, address, cancellationToken).ConfigureAwait(false);
                }

                if (!cancellationToken.IsCancellationRequested)
                    throw new EndOfStreamException("resolvectl monitor pipe closed");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "resolvectl monitor failed; retrying in {Delay}", delay);
                int? pid;
                lock (_gate)
                {
                    pid = _monitorPid;
                    _monitorPid = null;
                }

                await StopPrivilegedMonitorAsync(pid, CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (delay < TimeSpan.FromMinutes(1))
                    delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
            }
            finally
            {
                if (fifo is not null)
                    await fifo.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureFifoAsync(string fifoPath, CancellationToken cancellationToken)
    {
        if (File.Exists(fifoPath))
            return;

        var result = await processRunner.RunPrivilegedShellAsync(
                $"rm -f {ShellQuote(fifoPath)}; mkfifo {ShellQuote(fifoPath)}; chmod 666 {ShellQuote(fifoPath)}",
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.StandardError.Trim());
    }

    private string BuildStartMonitorScript(string fifoPath)
    {
        var fifo = ShellQuote(fifoPath);
        var pidFile = ShellQuote(PidFilePath);
        return $"""
            PIDFILE={pidFile}
            FIFO={fifo}
            if [ -f "$PIDFILE" ]; then
              OLD="$(cat "$PIDFILE" 2>/dev/null || true)"
              if [ -n "$OLD" ]; then
                pkill -TERM -P "$OLD" 2>/dev/null || true
                kill "$OLD" 2>/dev/null || true
              fi
              rm -f "$PIDFILE"
            fi
            if ! command -v resolvectl >/dev/null 2>&1; then
              echo "resolvectl not found" >&2
              exit 1
            fi
            if [ ! -p "$FIFO" ]; then
              rm -f "$FIFO"
              mkfifo "$FIFO"
              chmod 666 "$FIFO"
            fi
            # Child of the existing pkexec helper — no second password / polkit prompt.
            setsid bash -c 'resolvectl monitor >'"$FIFO"' 2>/dev/null' >/dev/null 2>&1 &
            echo $! | tee "$PIDFILE"
            """;
    }

    private async Task StopPrivilegedMonitorAsync(int? pid, CancellationToken cancellationToken)
    {
        if (pid is null or <= 0)
            return;

        try
        {
            await processRunner.RunPrivilegedShellAsync(
                    $"pkill -TERM -P {pid.Value} 2>/dev/null || true; kill {pid.Value} 2>/dev/null || true; sleep 0.1; pkill -KILL -P {pid.Value} 2>/dev/null || true; kill -9 {pid.Value} 2>/dev/null || true; rm -f {ShellQuote(PidFilePath)}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to stop resolvectl monitor pid {Pid}", pid);
        }
    }

    private string FifoPath => Path.Combine(appDataPaths.DataRoot, "resolvectl-monitor.fifo");

    private string PidFilePath => Path.Combine(appDataPaths.DataRoot, "resolvectl-monitor.pid");

    private Task OnResolvedAsync(string host, IPAddress address, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> suffixes;
        lock (_gate)
            suffixes = _suffixes;

        if (!DnsRouteSuffixes.Matches(host, suffixes))
            return Task.CompletedTask;

        if (address.AddressFamily == AddressFamily.InterNetwork)
            _queued[address + "/32"] = 0;
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            _queued[address + "/128"] = 0;

        return Task.CompletedTask;
    }

    private async Task RunFlusherAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                await FlushQueuedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resolved DNS route flush failed");
            }
        }
    }

    private async Task FlushQueuedAsync(CancellationToken cancellationToken)
    {
        if (_queued.IsEmpty)
            return;

        if (!await _flushLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            VpnProfile? profile;
            lock (_gate)
                profile = _profile;

            if (profile is null)
                return;

            var batch = _queued.Keys.ToArray();
            if (batch.Length == 0)
                return;

            foreach (var key in batch)
                _queued.TryRemove(key, out _);

            await policyRoutingSetup.AddHostRoutesAsync(profile, batch, cancellationToken)
                .ConfigureAwait(false);
            logger.LogDebug(
                "Resolved DNS monitor installed {Count} host routes for {Profile}",
                batch.Length,
                profile.Name);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    internal static bool TryParseResolvectlMonitorLine(
        string line,
        out string host,
        out IPAddress address)
    {
        host = string.Empty;
        address = IPAddress.None;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith("← A:", StringComparison.Ordinal) &&
            !trimmed.StartsWith("<- A:", StringComparison.Ordinal))
        {
            return false;
        }

        var payload = trimmed.StartsWith("← A:", StringComparison.Ordinal)
            ? trimmed["← A:".Length..].Trim()
            : trimmed["<- A:".Length..].Trim();

        var parts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
            return false;
        if (!string.Equals(parts[1], "IN", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(parts[2], "A", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parts[2], "AAAA", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[3], out var parsed))
            return false;

        host = parts[0].TrimEnd('.');
        address = parsed;
        return host.Length > 0;
    }

    private static async Task WaitQuietAsync(Task? task, CancellationToken cancellationToken)
    {
        if (task is null)
            return;

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
