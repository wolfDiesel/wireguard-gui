using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.Services;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.ViewModels;

internal sealed partial class ProfilesViewModel : LocalizedViewModelBase
{
    private readonly HandlerInvoker _invoker;
    private readonly AppToastService _toast;
    private readonly StatusBarService _statusBar;
    private DispatcherTimer? _pollTimer;
    private bool _refreshing;

    [ObservableProperty]
    private bool _setupRequired;

    [ObservableProperty]
    private string _nativeInstallFedora = string.Empty;

    [ObservableProperty]
    private string _nativeInstallDebian = string.Empty;

    [ObservableProperty]
    private string _nmcliInstallFedora = string.Empty;

    [ObservableProperty]
    private string _nmcliInstallDebian = string.Empty;

    [ObservableProperty]
    private ProfileRowViewModel? _selectedProfile;

    public SplitRoutingPanelViewModel SplitRouting { get; }

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = new();

    public string ProfilesTitle => T("Profiles_Title");
    public string ImportLabel => T("Profiles_Import");
    public string ConnectionLabel => T("Profiles_Connection");
    public string BackendLabel => T("Profiles_Backend");
    public string StatusLabel => T("Profiles_Status");
    public string ConnectLabel => T("Profiles_Connect");
    public string DisconnectLabel => T("Profiles_Disconnect");
    public string DeleteLabel => T("Profiles_Delete");
    public string SelectedSplitRoutingOnOff => SelectedProfile is null
        ? string.Empty
        : SelectedProfile.SplitRoutingEnabled ? T("On") : T("Off");
    public string SetupTitle => T("Profiles_Setup_Title");
    public string SetupBody => T("Profiles_Setup_Body");
    public string SetupNativeFedora => T("Profiles_Setup_Native_Fedora");
    public string SetupNativeDebian => T("Profiles_Setup_Native_Debian");
    public string SetupNmcliFedora => T("Profiles_Setup_Nmcli_Fedora");
    public string SetupNmcliDebian => T("Profiles_Setup_Nmcli_Debian");

    public ProfilesViewModel(
        HandlerInvoker invoker,
        AppToastService toast,
        StatusBarService statusBar,
        SplitRoutingPanelViewModel splitRouting,
        LocalizationService localization)
        : base(localization)
    {
        _invoker = invoker;
        _toast = toast;
        _statusBar = statusBar;
        SplitRouting = splitRouting;
    }

    public async Task InitializeAsync()
    {
        await RefreshCapabilitiesAsync();
        await RefreshProfilesAsync();
        StartPolling();
    }

    public void StopPolling()
    {
        if (_pollTimer is null)
            return;

        _pollTimer.Tick -= OnPollTick;
        _pollTimer.Stop();
        _pollTimer = null;
    }

    public bool CanConnectSelected => SelectedProfile is { IsConnected: false };
    public bool CanDisconnectSelected => SelectedProfile is { IsConnected: true };

    [RelayCommand]
    private async Task ConnectSelectedAsync()
    {
        if (SelectedProfile is null)
            return;
        await ConnectAsync(SelectedProfile);
    }

    [RelayCommand]
    private async Task DisconnectSelectedAsync()
    {
        if (SelectedProfile is null)
            return;
        await DisconnectAsync(SelectedProfile);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is null)
            return;
        await DeleteAsync(SelectedProfile);
    }

    public async Task ImportFromPickerAsync(Control owner)
    {
        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel?.StorageProvider is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Import_Picker_Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WireGuard") { Patterns = ["*.conf"] },
            ],
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;

        var capabilities = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetSystemCapabilitiesHandler>().HandleAsync());

        var window = owner as Window ?? TopLevel.GetTopLevel(owner) as Window;
        if (window is null)
            return;

        var dialog = new Views.ImportProfileDialog(capabilities, Localization);
        var backend = await dialog.ShowDialog<BackendKind?>(window);
        if (backend is null)
            return;

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<ImportProfileHandler>().HandleAsync(
                new ImportProfileRequestDto(path, backend.Value)));

        if (!result.Success)
        {
            _toast.ShowError(T("Toast_Import_Failed"), result.ErrorMessage);
            return;
        }

        _toast.ShowSuccess(T("Toast_Import_Success"));
        await RefreshProfilesAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync(ProfileRowViewModel? row)
    {
        if (row is null)
            return;

        if (SelectedProfile?.Id == row.Id)
            await SplitRouting.PersistBeforeConnectAsync();

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<ConnectProfileHandler>().HandleAsync(row.Id));

        if (!result.Success)
        {
            var message = OperationErrorMapper.ResolveMessage(Localization, result.ErrorCode, result.ErrorMessage);
            _toast.ShowError(T("Toast_Connect_Failed"), message);
            _statusBar.StatusText = message;
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.WarningMessage))
            _toast.ShowInfo(T("Toast_Connect_Success"), result.WarningMessage);
        else
            _toast.ShowSuccess(T("Toast_Connect_Success"), row.Name);
        row.State = ConnectionState.Connected;
        OnSelectedProfileActionsChanged();
        await PollProfileStatesAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync(ProfileRowViewModel? row)
    {
        if (row is null)
            return;

        if (SelectedProfile?.Id == row.Id)
            await SplitRouting.PersistBeforeConnectAsync();

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<DisconnectProfileHandler>().HandleAsync(row.Id));

        if (!result.Success)
        {
            var message = OperationErrorMapper.ResolveMessage(Localization, result.ErrorCode, result.ErrorMessage);
            _toast.ShowError(T("Toast_Disconnect_Failed"), message);
            return;
        }

        _toast.ShowSuccess(T("Toast_Disconnect_Success"), row.Name);
        row.State = ConnectionState.Disconnected;
        OnSelectedProfileActionsChanged();
        await PollProfileStatesAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(ProfileRowViewModel? row)
    {
        if (row is null)
            return;

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<DeleteProfileHandler>().HandleAsync(row.Id));

        if (!result.Success)
        {
            var message = OperationErrorMapper.ResolveMessage(Localization, result.ErrorCode, result.ErrorMessage);
            _toast.ShowError(T("Toast_Delete_Failed"), message);
            return;
        }

        Profiles.Remove(row);
        if (SelectedProfile?.Id == row.Id)
            SelectedProfile = null;

        if (string.IsNullOrWhiteSpace(result.WarningMessage))
            _toast.ShowSuccess(T("Toast_Delete_Success"));
        else
            _toast.ShowInfo(T("Toast_Delete_Success"), result.WarningMessage);
        await RefreshProfilesAsync();
    }

    partial void OnSelectedProfileChanged(ProfileRowViewModel? value)
    {
        OnSelectedProfileActionsChanged();
        OnPropertyChanged(nameof(SelectedSplitRoutingOnOff));
        SplitRouting.BindSelectedProfile(value);
    }

    private void OnSelectedProfileActionsChanged()
    {
        OnPropertyChanged(nameof(CanConnectSelected));
        OnPropertyChanged(nameof(CanDisconnectSelected));
    }

    public string? GetActiveConnectedProfileId() =>
        Profiles.FirstOrDefault(p => p.IsConnected)?.Id;

    public async Task ConnectActiveProfileAsync()
    {
        var id = GetActiveConnectedProfileId();
        var row = id is null
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(p => p.Id == id);

        if (row is not null && !row.IsConnected)
            await ConnectAsync(row);
    }

    public async Task DisconnectActiveProfileAsync()
    {
        var row = Profiles.FirstOrDefault(p => p.IsConnected);
        if (row is not null)
            await DisconnectAsync(row);
    }

    private void StartPolling()
    {
        StopPolling();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void OnPollTick(object? sender, EventArgs e) => _ = PollTickAsync();

    private async Task PollTickAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            await PollProfileStatesAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshCapabilitiesAsync()
    {
        var caps = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetSystemCapabilitiesHandler>().HandleAsync());

        SetupRequired = !caps.AnyAvailable;

        var native = caps.Backends.FirstOrDefault(b => b.Backend == BackendKind.Native);
        var nmcli = caps.Backends.FirstOrDefault(b => b.Backend == BackendKind.Nmcli);

        NativeInstallFedora = native?.FedoraInstallHint ?? string.Empty;
        NativeInstallDebian = native?.DebianInstallHint ?? string.Empty;
        NmcliInstallFedora = nmcli?.FedoraInstallHint ?? string.Empty;
        NmcliInstallDebian = nmcli?.DebianInstallHint ?? string.Empty;
    }

    private async Task PollProfileStatesAsync()
    {
        var items = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetProfilesHandler>().HandleAsync());

        foreach (var item in items)
        {
            var row = Profiles.FirstOrDefault(p => p.Id == item.Id);
            if (row is null)
                continue;

            row.Name = item.Name;
            row.ConnectionName = item.ConnectionName;
            row.Backend = item.Backend;
            row.State = item.State;
            row.SplitRoutingEnabled = item.SplitRoutingEnabled;
            if (ReferenceEquals(SelectedProfile, row))
                OnSelectedProfileActionsChanged();
        }

        UpdateStatusBar(items);
    }

    private async Task RefreshProfilesAsync()
    {
        var items = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetProfilesHandler>().HandleAsync());

        var selected = SelectedProfile;
        ProfileListSynchronizer.Reconcile(Profiles, items, ref selected, Localization);
        SelectedProfile = selected;
        UpdateStatusBar(items);
    }

    private void UpdateStatusBar(IReadOnlyList<ProfileSummaryDto> items)
    {
        var connected = items.FirstOrDefault(p => p.State == ConnectionState.Connected);
        _statusBar.IsConnected = connected is not null;
        _statusBar.StatusText = connected is not null
            ? Tf("Status_Connected", connected.Name)
            : items.Count == 0
                ? T("Status_NoProfiles")
                : T("Status_Disconnected");
    }

    protected override void OnLocalizationChanged()
    {
        NotifyLocalized(
            nameof(ProfilesTitle),
            nameof(ImportLabel),
            nameof(ConnectionLabel),
            nameof(BackendLabel),
            nameof(StatusLabel),
            nameof(ConnectLabel),
            nameof(DisconnectLabel),
            nameof(DeleteLabel),
            nameof(SelectedSplitRoutingOnOff),
            nameof(SetupTitle),
            nameof(SetupBody),
            nameof(SetupNativeFedora),
            nameof(SetupNativeDebian),
            nameof(SetupNmcliFedora),
            nameof(SetupNmcliDebian));

        foreach (var profile in Profiles)
            profile.RefreshLocalization();

        OnPropertyChanged(nameof(SelectedSplitRoutingOnOff));

        var items = Profiles.Select(p => new ProfileSummaryDto(
            p.Id,
            p.Name,
            p.ConnectionName,
            p.Backend,
            p.State,
            p.SplitRoutingEnabled)).ToList();
        UpdateStatusBar(items);
    }
}
