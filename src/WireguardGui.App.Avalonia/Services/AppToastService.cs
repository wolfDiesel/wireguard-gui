using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WireguardGui.App.Avalonia.Services;

internal enum AppToastVariant
{
    Success,
    Error,
    Info,
}

internal sealed partial class AppToastItemViewModel : ObservableObject
{
    private readonly Action<string> _dismiss;
    private readonly Action<string> _pause;
    private readonly Action<string> _resume;

    public AppToastItemViewModel(
        string id,
        string title,
        AppToastVariant variant,
        string? description,
        Action<string> dismiss,
        Action<string> pause,
        Action<string> resume)
    {
        Id = id;
        Title = title;
        Variant = variant;
        Description = description;
        _dismiss = dismiss;
        _pause = pause;
        _resume = resume;
    }

    public string Id { get; }

    public string Title { get; }

    public string? Description { get; }

    public AppToastVariant Variant { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public void PauseTimer() => _pause(Id);

    public void ResumeTimer() => _resume(Id);

    [RelayCommand]
    private void Close() => _dismiss(Id);
}

internal sealed class AppToastService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    private readonly Dictionary<string, DispatcherTimer> _timers = new(StringComparer.Ordinal);

    public ObservableCollection<AppToastItemViewModel> Items { get; } = new();

    public void ShowSuccess(string title, string? description = null) =>
        Show(title, AppToastVariant.Success, description);

    public void ShowError(string title, string? description = null) =>
        Show(title, AppToastVariant.Error, description);

    public void ShowInfo(string title, string? description = null) =>
        Show(title, AppToastVariant.Info, description);

    public void Show(string title, AppToastVariant variant, string? description = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(title, variant, description));
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var item = new AppToastItemViewModel(id, title, variant, description, Dismiss, Pause, Resume);
        Items.Add(item);
        StartTimeout(id);
    }

    public void Pause(string id)
    {
        if (_timers.Remove(id, out var timer))
            timer.Stop();
    }

    public void Resume(string id)
    {
        if (_timers.ContainsKey(id) || !Items.Any(toast => toast.Id == id))
            return;

        StartTimeout(id);
    }

    private void StartTimeout(string id)
    {
        var timer = new DispatcherTimer { Interval = DefaultTimeout };
        timer.Tick += (_, _) => Dismiss(id);
        _timers[id] = timer;
        timer.Start();
    }

    private void Dismiss(string id)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Dismiss(id));
            return;
        }

        if (_timers.Remove(id, out var timer))
            timer.Stop();

        var item = Items.FirstOrDefault(toast => toast.Id == id);
        if (item is not null)
            Items.Remove(item);
    }
}
