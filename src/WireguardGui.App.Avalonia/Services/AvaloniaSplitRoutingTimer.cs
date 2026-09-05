using Avalonia.Threading;
using WireguardGui.Application.Abstractions;

namespace WireguardGui.App.Avalonia.Services;

/// <summary>
/// Реализация <see cref="ISplitRoutingTimer"/> на Avalonia DispatcherTimer.
/// UI-слой предоставляет таймер, бизнес-логика живёт в Application.
/// </summary>
internal sealed class AvaloniaSplitRoutingTimer : ISplitRoutingTimer
{
    private readonly DispatcherTimer _timer;

    public AvaloniaSplitRoutingTimer()
    {
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}