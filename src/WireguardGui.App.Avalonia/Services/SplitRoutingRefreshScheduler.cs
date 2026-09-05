using WireguardGui.Application.Abstractions;

namespace WireguardGui.App.Avalonia.Services;

/// <summary>
/// Тонкая UI-обёртка над <see cref="SplitRoutingRefreshService"/> (Application).
/// Вся бизнес-логика фонового refresh живёт в Application-слое (пункт 7 анализа).
/// </summary>
internal sealed class SplitRoutingRefreshScheduler(
    ISplitRoutingRefreshScheduler inner) : ISplitRoutingRefreshScheduler
{
    public void NotifyProfileConnected(string profileId) => inner.NotifyProfileConnected(profileId);

    public void NotifyProfileDisconnected(string? profileId = null) =>
        inner.NotifyProfileDisconnected(profileId);

    public void Stop() => inner.Stop();

    public IDisposable BeginManualApply() => inner.BeginManualApply();

    public void ApplyRefreshInterval(int minutes) => inner.ApplyRefreshInterval(minutes);

    public void RequestForceRefresh() => inner.RequestForceRefresh();
}
