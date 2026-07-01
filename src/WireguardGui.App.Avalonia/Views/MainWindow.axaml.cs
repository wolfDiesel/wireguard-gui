using Avalonia.Controls;
using WireguardGui.App.Avalonia.Services;

namespace WireguardGui.App.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ToastHost.DataContext = AppServices.GetRequired<AppToastService>();
    }
}
