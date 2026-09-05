namespace WireguardGui.Application.Abstractions;

/// <summary>
/// Абстракция таймера для фонового refresh split routing.
/// Позволяет Application-слою не зависеть от Avalonia DispatcherTimer.
/// </summary>
public interface ISplitRoutingTimer
{
    /// <summary>Интервал срабатывания.</summary>
    TimeSpan Interval { get; set; }

    /// <summary>Событие срабатывания таймера.</summary>
    event EventHandler? Tick;

    /// <summary>Запускает таймер.</summary>
    void Start();

    /// <summary>Останавливает таймер.</summary>
    void Stop();
}