using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Services;

internal sealed class SplitRoutingRefreshScheduler : ISplitRoutingRefreshScheduler, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly HandlerInvoker _invoker;
    private readonly AppToastService _toast;
    private readonly LocalizationService _localization;
    private readonly ILogger<SplitRoutingRefreshScheduler> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private DispatcherTimer? _timer;
    private string? _watchedProfileId;
    private int _manualApplyDepth;
    private bool _disposed;
    private TimeSpan _interval = TimeSpan.FromMinutes(AppSettings.DefaultRefreshMinutes);

    public SplitRoutingRefreshScheduler(
        IServiceProvider services,
        HandlerInvoker invoker,
        AppToastService toast,
        LocalizationService localization,
        ILogger<SplitRoutingRefreshScheduler> logger)
    {
        _services = services;
        _invoker = invoker;
        _toast = toast;
        _localization = localization;
        _logger = logger;
        _ = LoadIntervalAsync();
    }

    public void ApplyRefreshInterval(int minutes)
    {
        var clamped = AppSettings.ClampRefreshMinutes(minutes);
        lock (_gate)
            _interval = TimeSpan.FromMinutes(clamped);

        Dispatcher.UIThread.Post(() =>
        {
            lock (_gate)
            {
                if (_timer is not null)
                    _timer.Interval = _interval;
            }
        });

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
    }

    public void Stop()
    {
        lock (_gate)
        {
            _watchedProfileId = null;
            StopTimerLocked();
        }
    }

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
        Stop();
        _runLock.Dispose();
        return ValueTask.CompletedTask;
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
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (_gate)
            {
                if (!string.Equals(_watchedProfileId, profileId, StringComparison.Ordinal))
                    return;
                EnsureTimerLocked();
            }
        });
    }

    private void EnsureTimerLocked()
    {
        if (_timer is not null)
        {
            _timer.Interval = _interval;
            return;
        }

        _timer = new DispatcherTimer { Interval = _interval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimerLocked()
    {
        if (_timer is null)
            return;

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e) => _ = RefreshTickAsync();

    private async Task RefreshTickAsync()
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
            if (profile is null || !profile.SplitRouting.NeedsDnsRouteRefresh)
                return;

            var backend = _services.GetRequiredService<IWireGuardBackendFactory>().GetBackend(profile.Backend);
            var state = await backend.GetConnectionStateAsync(profile).ConfigureAwait(false);
            if (state != ConnectionState.Connected)
            {
                NotifyProfileDisconnected(profileId);
                return;
            }

            _logger.LogInformation("Background split routing refresh for {Profile}", profile.Name);
            var result = await _invoker.InvokeAsync(sp =>
                sp.GetRequiredService<ApplySplitRoutingHandler>().HandleAsync(profileId)).ConfigureAwait(false);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Background split routing refresh failed for {Profile}: {Error}",
                    profile.Name,
                    result.ErrorMessage);
                return;
            }

            if (result.RoutesCsv is null)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
                _toast.ShowInfo(
                    _localization.Get("Toast_Routes_Refreshed"),
                    _localization.Format("Toast_Routes_Refreshed_Detail", result.RouteCount)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background split routing refresh error");
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

    private sealed class ManualApplyScope(SplitRoutingRefreshScheduler owner) : IDisposable
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
