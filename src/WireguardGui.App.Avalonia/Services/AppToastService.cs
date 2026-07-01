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

    public AppToastItemViewModel(
        string id,
        string title,
        AppToastVariant variant,
        string? description,
        Action<string> dismiss)
    {
        Id = id;
        Title = title;
        Variant = variant;
        Description = description;
        _dismiss = dismiss;
    }

    public string Id { get; }

    public string Title { get; }

    public string? Description { get; }

    public AppToastVariant Variant { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [RelayCommand]
    private void Close() => _dismiss(Id);
}

internal sealed class AppToastService
{
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
        var id = Guid.NewGuid().ToString("N");
        var item = new AppToastItemViewModel(id, title, variant, description, Dismiss);
        Items.Add(item);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _timers.Remove(id);
            Dismiss(id);
        };
        _timers[id] = timer;
        timer.Start();
    }

    private void Dismiss(string id)
    {
        if (_timers.Remove(id, out var timer))
            timer.Stop();

        var item = Items.FirstOrDefault(toast => toast.Id == id);
        if (item is not null)
            Items.Remove(item);
    }
}
