using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

/// <summary>
/// Мониторинг DNS-резолвов через systemd-resolved D-Bus-сокет
/// (<see cref="SystemdResolvedMonitorClient"/>). Выбран как единственный
/// механизм (пункт 8 анализа): не требует pkexec/FIFO и работает
/// на любом стеке с systemd-resolved.
/// </summary>
public sealed class ResolvedDnsRouteMonitor(
    IPolicyRoutingSetup policyRoutingSetup,
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
        if (!policyRoutingSetup.IsAvailable)
        {
            logger.LogWarning(
                "Resolved DNS route monitor skipped for {Profile}: ip (policy routing) is not available",
                profile.Name);
            await StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (suffixes.Count == 0)
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

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            flusher = _flusher;
            name = _profile?.Name;
            _cts = null;
            _loop = null;
            _flusher = null;
            _profile = null;
            _suffixes = [];
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
            try
            {
                await SystemdResolvedMonitorClient.SubscribeAsync(
                    OnResolvedAsync,
                    logger,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "resolved D-Bus monitor failed; retrying in {Delay}", delay);
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
        }
    }

    private Task OnResolvedAsync(
        string host,
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> suffixes;
        lock (_gate)
            suffixes = _suffixes;

        if (!DnsRouteSuffixes.Matches(host, suffixes))
            return Task.CompletedTask;

        foreach (var address in addresses)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
                _queued[address + "/32"] = 0;
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
                _queued[address + "/128"] = 0;
        }

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
