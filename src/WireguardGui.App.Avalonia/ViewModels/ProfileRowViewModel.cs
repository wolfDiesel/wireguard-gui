using CommunityToolkit.Mvvm.ComponentModel;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.ViewModels;

internal sealed partial class ProfileRowViewModel : LocalizedViewModelBase
{
    public ProfileRowViewModel(
        LocalizationService localization,
        string id,
        string name,
        string connectionName,
        BackendKind backend,
        ConnectionState state,
        bool splitRoutingEnabled)
        : base(localization)
    {
        Id = id;
        Name = name;
        ConnectionName = connectionName;
        Backend = backend;
        State = state;
        SplitRoutingEnabled = splitRoutingEnabled;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _connectionName;

    [ObservableProperty]
    private BackendKind _backend;

    [ObservableProperty]
    private ConnectionState _state;

    [ObservableProperty]
    private bool _splitRoutingEnabled;

    public string BackendLabel => Backend switch
    {
        BackendKind.Native => T("Backend_Native"),
        BackendKind.Nmcli => T("Backend_Nmcli"),
        _ => Backend.ToString(),
    };

    public string ConnectionLabel => Tf("Connection_Label", ConnectionName);

    public string StateLabel => State switch
    {
        ConnectionState.Connected => T("State_Connected"),
        ConnectionState.Disconnected => T("State_Disconnected"),
        _ => T("State_Unknown"),
    };

    public bool IsConnected => State == ConnectionState.Connected;

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(IsConnected));
    }

    partial void OnConnectionNameChanged(string value) =>
        OnPropertyChanged(nameof(ConnectionLabel));

    partial void OnBackendChanged(BackendKind value) =>
        OnPropertyChanged(nameof(BackendLabel));

    protected override void OnLocalizationChanged()
    {
        NotifyLocalized(nameof(BackendLabel), nameof(ConnectionLabel), nameof(StateLabel));
    }

    internal void RefreshLocalization() => OnLocalizationChanged();
}
