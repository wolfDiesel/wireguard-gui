namespace WireguardGui.Application.Abstractions;

/// <summary>
/// Уведомления о фоновом refresh split routing.
/// Реализуется UI-слоем (тосты), чтобы Application не зависел от презентации.
/// </summary>
public interface ISplitRoutingRefreshNotifier
{
    /// <summary>Показать уведомление об успешном обновлении маршрутов.</summary>
    void NotifyRoutesRefreshed(int routeCount);
}