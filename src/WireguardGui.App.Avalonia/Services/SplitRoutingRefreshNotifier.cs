using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Application.Abstractions;

namespace WireguardGui.App.Avalonia.Services;

/// <summary>
/// UI-реализация уведомлений о фоновом refresh split routing (тосты).
/// </summary>
internal sealed class SplitRoutingRefreshNotifier(
    AppToastService toast,
    LocalizationService localization) : ISplitRoutingRefreshNotifier
{
    public void NotifyRoutesRefreshed(int routeCount)
    {
        toast.ShowInfo(
            localization.Get("Toast_Routes_Refreshed"),
            localization.Format("Toast_Routes_Refreshed_Detail", routeCount));
    }
}