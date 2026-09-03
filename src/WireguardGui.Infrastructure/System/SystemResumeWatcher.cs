using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;

namespace WireguardGui.Infrastructure.System;

public sealed class SystemResumeWatcher : ISystemResumeWatcher, IAsyncDisposable
{
    private readonly ILogger<SystemResumeWatcher> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Func<CancellationToken, Task>? _onResumed;

    public SystemResumeWatcher(ILogger<SystemResumeWatcher> logger)
    {
        _logger = logger;
    }

    public void Start(Func<CancellationToken, Task> onResumed)
    {
        ArgumentNullException.ThrowIfNull(onResumed);

        lock (_gate)
        {
            _onResumed = onResumed;
            if (_loop is { IsCompleted: false })
                return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        }

        _logger.LogInformation("System resume watcher started");
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
            _onResumed = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WatchOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Resume watcher cycle failed; retrying");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task WatchOnceAsync(CancellationToken cancellationToken)
    {
        var psi = new global::System.Diagnostics.ProcessStartInfo
        {
            FileName = "dbus-monitor",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--system");
        psi.ArgumentList.Add(
            "type='signal',interface='org.freedesktop.login1.Manager',member='PrepareForSleep'");

        using var process = new global::System.Diagnostics.Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "dbus-monitor unavailable; resume auto-refresh disabled");
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;

                if (!line.Contains("boolean", StringComparison.OrdinalIgnoreCase))
                    continue;

                var waking = line.Contains("false", StringComparison.OrdinalIgnoreCase);
                if (!waking)
                    continue;

                _logger.LogInformation("System resume detected; requesting split routing refresh");
                Func<CancellationToken, Task>? handler;
                lock (_gate)
                    handler = _onResumed;

                if (handler is null)
                    continue;

                try
                {
                    await handler(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Resume refresh handler failed");
                }
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }
}
