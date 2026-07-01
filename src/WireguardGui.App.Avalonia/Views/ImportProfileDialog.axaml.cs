using Avalonia.Controls;
using Avalonia.Interactivity;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Views;

public partial class ImportProfileDialog : Window
{
    private readonly SystemCapabilitiesDto _capabilities;
    private readonly LocalizationService _localization;

    public ImportProfileDialog(SystemCapabilitiesDto capabilities, LocalizationService localization)
    {
        _capabilities = capabilities;
        _localization = localization;
        InitializeComponent();
        ApplyLocalizedStrings();
        ConfigureBackends();
    }

    private void ApplyLocalizedStrings()
    {
        Title = _localization.Get("Import_Dialog_Title");
        DialogTitle.Text = _localization.Get("Import_Dialog_Subtitle");
        NativeRadio.Content = _localization.Get("Import_Backend_Native");
        NmcliRadio.Content = _localization.Get("Import_Backend_Nmcli");
        CancelButton.Content = _localization.Get("Import_Dialog_Cancel");
        ImportButton.Content = _localization.Get("Import_Dialog_Import");
    }

    private void ConfigureBackends()
    {
        var native = _capabilities.Backends.FirstOrDefault(b => b.Backend == BackendKind.Native);
        var nmcli = _capabilities.Backends.FirstOrDefault(b => b.Backend == BackendKind.Nmcli);

        if (native?.IsAvailable == true)
        {
            NativeRadio.IsChecked = true;
        }
        else
        {
            NativeRadio.IsEnabled = false;
            NativeWarning.IsVisible = true;
            NativeWarning.Text = native is null
                ? _localization.Get("Import_Native_Unavailable")
                : _localization.Format(
                    "Import_Missing_Components",
                    string.Join(", ", native.MissingComponents));
        }

        if (nmcli?.IsAvailable == true)
        {
            if (native?.IsAvailable != true)
                NmcliRadio.IsChecked = true;
        }
        else
        {
            NmcliRadio.IsEnabled = false;
            NmcliWarning.IsVisible = true;
            NmcliWarning.Text = nmcli is null
                ? _localization.Get("Import_Nmcli_Unavailable")
                : _localization.Format(
                    "Import_Missing_Components",
                    string.Join(", ", nmcli.MissingComponents));
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (NativeRadio.IsChecked == true && NativeRadio.IsEnabled)
        {
            Close(BackendKind.Native);
            return;
        }

        if (NmcliRadio.IsChecked == true && NmcliRadio.IsEnabled)
        {
            Close(BackendKind.Nmcli);
            return;
        }

        Close(null);
    }
}
