using Avalonia.Controls;
using Avalonia.Interactivity;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Views;

public partial class ImportProfileDialog : Window
{
    private readonly SystemCapabilitiesDto _capabilities;
    private readonly ConfigKind _configKind;
    private readonly LocalizationService _localization;

    public ImportProfileDialog(
        SystemCapabilitiesDto capabilities,
        ConfigKind configKind,
        LocalizationService localization)
    {
        _capabilities = capabilities;
        _configKind = configKind;
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
        AmneziaWgRadio.Content = _localization.Get("Import_Backend_AmneziaWg");
        CancelButton.Content = _localization.Get("Import_Dialog_Cancel");
        ImportButton.Content = _localization.Get("Import_Dialog_Import");
    }

    private void ConfigureBackends()
    {
        var native = GetCapability(BackendKind.Native);
        var nmcli = GetCapability(BackendKind.Nmcli);
        var amneziaWg = GetCapability(BackendKind.AmneziaWg);

        ConfigureBackend(native, NativeRadio, NativeWarning, "Import_Native_Unavailable");
        ConfigureBackend(nmcli, NmcliRadio, NmcliWarning, "Import_Nmcli_Unavailable");
        ConfigureBackend(amneziaWg, AmneziaWgRadio, AmneziaWgWarning, "Import_AmneziaWg_Unavailable");

        if (_configKind == ConfigKind.AmneziaWg && amneziaWg?.IsAvailable != true)
        {
            ConfigKindHint.IsVisible = true;
            ConfigKindHint.Text = _localization.Get("Import_AmneziaWg_Required");
            ImportButton.IsEnabled = false;
        }

        if (_configKind == ConfigKind.AmneziaWg && amneziaWg?.IsAvailable == true)
            AmneziaWgRadio.IsChecked = true;
        else if (native?.IsAvailable == true)
            NativeRadio.IsChecked = true;
        else if (nmcli?.IsAvailable == true)
            NmcliRadio.IsChecked = true;
        else if (amneziaWg?.IsAvailable == true)
            AmneziaWgRadio.IsChecked = true;
    }

    private BackendCapabilityDto? GetCapability(BackendKind backend) =>
        _capabilities.Backends.FirstOrDefault(capability => capability.Backend == backend);

    private void ConfigureBackend(
        BackendCapabilityDto? capability,
        RadioButton radio,
        TextBlock warning,
        string unavailableKey)
    {
        if (capability?.IsAvailable == true)
            return;

        radio.IsEnabled = false;
        warning.IsVisible = true;
        warning.Text = BuildWarningText(capability, unavailableKey);
    }

    private string BuildWarningText(BackendCapabilityDto? capability, string unavailableKey)
    {
        if (capability is null)
            return _localization.Get(unavailableKey);

        var lines = new List<string>
        {
            _localization.Format("Import_Missing_Components", string.Join(", ", capability.MissingComponents)),
        };

        if (!string.IsNullOrWhiteSpace(capability.FedoraInstallHint))
            lines.Add(capability.FedoraInstallHint);
        if (!string.IsNullOrWhiteSpace(capability.DebianInstallHint))
            lines.Add(capability.DebianInstallHint);

        return string.Join(Environment.NewLine, lines);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (NativeRadio.IsChecked == true && NativeRadio.IsEnabled)
            Close(BackendKind.Native);
        else if (NmcliRadio.IsChecked == true && NmcliRadio.IsEnabled)
            Close(BackendKind.Nmcli);
        else if (AmneziaWgRadio.IsChecked == true && AmneziaWgRadio.IsEnabled)
            Close(BackendKind.AmneziaWg);
        else
            Close(null);
    }
}
