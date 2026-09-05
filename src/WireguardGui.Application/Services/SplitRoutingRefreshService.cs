using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;

namespace WireguardGui.Application.Services;

/// <summary>
/// Фоновый refresh split routing для подключённого профиля.
/// Перенесён из UI-слоя (App.Avalonia) в Application (пункт 7 анализа):
/// бизнес-логика таймера живёт в Application, UI остаётся тонкой обёрткой.
/// </summary>
public sealed class SplitRoutingRefreshService : ISplitRoutingRefreshScheduler, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISplitRoutingTimer _timer;
    private readonly ISystemResumeWatcher _resumeWatcher;
    private readonly IResolvedDnsRouteMonitor _dnsRouteMonitor;
    private readonly ISplitRoutingRefreshNotifier _notifier;
    private readonly ILogger<SplitRoutingRefreshService> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private string? _watchedProfileId;
    private int _manualApplyDepth;
    private bool _disposed;
    private bool _resumeHooked;
    private TimeSpan _interval = TimeSpan.FromMinutes(AppSettings.DefaultRefreshMinutes);

    public SplitRoutingRefreshService(
        IServiceProvider services,
        ISplitRoutingTimer timer,
        ISystemResumeWatcher resumeWatcher,
        IResolvedDnsRouteMonitor dnsRouteMonitor,
        ISplitRoutingRefreshNotifier notifier,
        ILogger<SplitRoutingRefreshService> logger)
    {
        _services = services;
        _timer = timer;
        _resumeWatcher = resumeWatcher;
        _dnsRouteMonitor = dnsRouteMonitor;
        _notifier = notifier;
        _logger = logger;
        _timer.Tick += OnTick;
        _ = LoadIntervalAsync();
        EnsureResumeHook();
    }

    public void ApplyRefreshInterval(int minutes)
    {
        var clamped = AppSettings.ClampRefreshMinutes(minutes);
        lock (_gate)
            _interval = TimeSpan.FromMinutes(clamped);

        _timer.Interval = _interval;
        _logger.LogInformation("Split routing refresh interval set to {Minutes} minutes", clamped);
    }

    public void NotifyProfileConnected(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        lock (_gate)
        {
            _watchedProfileId = profileId;
        }

        _ = StartWatchingAsync(profileId);
        _ = StartDnsMonitorAsync(profileId);

        _logger.LogInformation("Split routing refresh watching profile {ProfileId}", profileId);
    }

    public void NotifyProfileDisconnected(string? profileId = null)
    {
        lock (_gate)
        {
            if (profileId is not null
                && _watchedProfileId is not null
                && !string.Equals(_watchedProfileId, profileId, StringComparison.Ordinal))
            {
                return;
            }

            _watchedProfileId = null;
            StopTimerLocked();
        }

        _ = _dnsRouteMonitor.StopAsync();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _watchedProfileId = null;
            StopTimerLocked();
        }

        _ = _dnsRouteMonitor.StopAsync();
    }

    public void RequestForceRefresh() => _ = RefreshTickAsync(force: true);

    public IDisposable BeginManualApply()
    {
        lock (_gate)
            _manualApplyDepth++;

        return new ManualApplyScope(this);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _timer.Tick -= OnTick;
        Stop();
        _runLock.Dispose();
        return _dnsRouteMonitor is IAsyncDisposable disposable
            ? disposable.DisposeAsync()
            : ValueTask.CompletedTask;
    }

    private async Task StartDnsMonitorAsync(string profileId)
    {
        try
        {
            var store = _services.GetRequiredService<IProfileStore>();
            var profile = await store.GetProfileAsync(profileId).ConfigureAwait(false);
            if (profile is null)
                return;

            lock (_gate)
            {
                if (!string.Equals(_watchedProfileId, profileId, StringComparison.Ordinal))
                    return;
            }

            await _dnsRouteMonitor.StartAsync(profile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start resolved DNS route monitor");
        }
    }

    private void EnsureResumeHook()
    {
        lock (_gate)
        {
            if (_resumeHooked)
                return;
            _resumeHooked = true;
        }

        _resumeWatcher.Start(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            await RefreshTickAsync(force: true).ConfigureAwait(false);
        });
    }

    private async Task LoadIntervalAsync()
    {
        try
        {
            var settings = await _services.GetRequiredService<ISettingsStore>()
                .LoadAsync()
                .ConfigureAwait(false);
            ApplyRefreshInterval(settings.SplitRoutingRefreshMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load split routing refresh interval");
        }
    }

    private async Task StartWatchingAsync(string profileId)
    {
        await LoadIntervalAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (!string.Equals(_watchedProfileId, profileId, StringComparison.Ordinal))
                return;
            EnsureTimerLocked();
        }
    }

    private void EnsureTimerLocked()
    {
        _timer.Interval = _interval;
        _timer.Start();
    }

    private void StopTimerLocked()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e) => _ = RefreshTickAsync(force: false);

    private async Task RefreshTickAsync(bool force)
    {
        string? profileId;
        lock (_gate)
        {
            if (_manualApplyDepth > 0 || _watchedProfileId is null)
                return;
            profileId = _watchedProfileId;
        }

        if (!await _runLock.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            var store = _services.GetRequiredService<IProfileStore>();
            var profile = await store.GetProfileAsync(profileId).ConfigureAwait(false);
            if (profile is null)
                return;

            if (!force && !profile.SplitRouting.NeedsDnsRouteRefresh)
                return;

            if (!profile.SplitRouting.Enabled)
                return;

            var backend = _services.GetRequiredService<IWireGuardBackendFactory>().GetBackend(profile.Backend);
            var state = await backend.GetConnectionStateAsync(profile).ConfigureAwait(false);
            if (state != ConnectionState.Connected)
            {
                NotifyProfileDisconnected(profileId);
                return;
            }

            _logger.LogInformation(
                "{Mode} split routing refresh for {Profile}",
                force ? "Forced" : "Background",
                profile.Name);

            await using var scope = _services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ApplySplitRoutingHandler>();
            var result = await handler.HandleAsync(profileId, forceRefresh: force).ConfigureAwait(false);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Split routing refresh failed for {Profile}: {Error}",
                    profile.Name,
                    result.ErrorMessage);
                return;
            }

            await _dnsRouteMonitor.StartAsync(profile).ConfigureAwait(false);

            if (result.RoutesCsv is null)
                return;

            _notifier.NotifyRoutesRefreshed(result.RouteCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Split routing refresh error");
        }
        finally
        {
            _runLock.Release();
        }
    }

    private void EndManualApply()
    {
        lock (_gate)
        {
            if (_manualApplyDepth > 0)
                _manualApplyDepth--;
        }
    }

    private sealed class ManualApplyScope(SplitRoutingRefreshService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.EndManualApply();
        }
    }
}