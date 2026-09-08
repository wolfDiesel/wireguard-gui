using Avalonia.Controls;
using Avalonia.Input;
using WireguardGui.App.Avalonia.Services;

namespace WireguardGui.App.Avalonia.Views;

public partial class AppToastHost : UserControl
{
    public AppToastHost()
    {
        InitializeComponent();
    }

    private void OnToastPointerEntered(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppToastItemViewModel item)
            item.PauseTimer();
    }

    private void OnToastPointerExited(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is AppToastItemViewModel item)
            item.ResumeTimer();
    }
}
