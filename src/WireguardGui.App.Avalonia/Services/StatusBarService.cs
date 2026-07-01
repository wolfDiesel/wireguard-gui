using CommunityToolkit.Mvvm.ComponentModel;

namespace WireguardGui.App.Avalonia.Services;

internal sealed partial class StatusBarService : ObservableObject
{
    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Отключён";
}
