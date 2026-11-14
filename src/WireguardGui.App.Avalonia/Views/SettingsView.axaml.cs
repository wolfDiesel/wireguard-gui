using Avalonia.Controls;
using Avalonia.Interactivity;
using WireguardGui.App.Avalonia.ViewModels;

namespace WireguardGui.App.Avalonia.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnInstallAmneziaWgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.InstallAmneziaWgAsync(this);
    }

    private async void OnManualInstallClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.ShowManualInstallAsync(this);
    }
}
