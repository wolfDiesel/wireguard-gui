using Avalonia.Controls;
using Avalonia.Interactivity;
using WireguardGui.App.Avalonia.ViewModels;

namespace WireguardGui.App.Avalonia.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilesViewModel vm)
            await vm.ImportFromPickerAsync(this);
    }
}
