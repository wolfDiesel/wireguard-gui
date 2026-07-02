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
    private bool _splitRoutingApplyInProgress;
    private bool _loadingSplitRouting;
    private string? _loadedSplitRoutingProfileId;
    private SplitRoutingSettings? _savedSplitRouting;

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

    [ObservableProperty]
    private bool _splitRoutingEnabled;

    public bool SplitRoutingOptionsEnabled => SplitRoutingEnabled;

    [ObservableProperty]
    private bool _splitYoutube = true;

    [ObservableProperty]
    private bool _splitTelegram = true;

    [ObservableProperty]
    private bool _splitTwitch;

    [ObservableProperty]
    private bool _splitCloudflare;

    [ObservableProperty]
    private string _customDomainsText = string.Empty;

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = new();

    public string ProfilesTitle => T("Profiles_Title");
    public string ImportLabel => T("Profiles_Import");
    public string ConnectionLabel => T("Profiles_Connection");
    public string BackendLabel => T("Profiles_Backend");
    public string StatusLabel => T("Profiles_Status");
    public string ConnectLabel => T("Profiles_Connect");
    public string DisconnectLabel => T("Profiles_Disconnect");
    public string DeleteLabel => T("Profiles_Delete");
    public string SplitRoutingLabel => T("Profiles_SplitRouting");
    public string SplitEnableLabel => T("Profiles_Split_Enable");
    public string SplitYoutubeLabel => T("Profiles_Split_Youtube");
    public string SplitTelegramLabel => T("Profiles_Split_Telegram");
    public string SplitTwitchLabel => T("Profiles_Split_Twitch");
    public string SplitCloudflareLabel => T("Profiles_Split_Cloudflare");
    public string SplitDomainsLabel => T("Profiles_Split_Domains");
    public string SplitApplyLabel => T("Profiles_Split_Apply");
    public string SplitHintCloudflare => T("Profiles_Split_Hint_Cloudflare");
    public string SplitHintTwitch => T("Profiles_Split_Hint_Twitch");
    public string SplitHintReconnect => T("Profiles_Split_Hint_Reconnect");
    public string SplitSyncingLabel => T("Profiles_Split_Syncing");
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
        LocalizationService localization)
        : base(localization)
    {
        _invoker = invoker;
        _toast = toast;
        _statusBar = statusBar;
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

    public bool CanConnectSelected => SelectedProfile is { IsConnected: false };
    public bool CanDisconnectSelected => SelectedProfile is { IsConnected: true };
    public bool CanApplySplitRouting =>
        SelectedProfile is not null && SplitRoutingEnabled && HasUnappliedSplitRoutingChanges && !IsApplyingRoutes;

    [ObservableProperty]
    private bool _hasUnappliedSplitRoutingChanges;

    [ObservableProperty]
    private bool _isApplyingRoutes;

    [ObservableProperty]
    private string _applyRoutesStatus = string.Empty;

    public bool ShowReconnectHint =>
        SelectedProfile is { IsConnected: true } && SplitRoutingEnabled;

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
            await PersistSplitRoutingFromUiAsync();

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<ConnectProfileHandler>().HandleAsync(row.Id));

        if (!result.Success)
        {
            _toast.ShowError(T("Toast_Connect_Failed"), result.ErrorMessage);
            _statusBar.StatusText = result.ErrorMessage ?? T("Status_Error");
            return;
        }

        _toast.ShowSuccess(T("Toast_Connect_Success"), row.Name);
        row.State = ConnectionState.Connected;
        OnSelectedProfileActionsChanged();
        CaptureSavedSplitRoutingFromUi();
        await PollProfileStatesAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync(ProfileRowViewModel? row)
    {
        if (row is null)
            return;

        if (SelectedProfile?.Id == row.Id)
            await PersistSplitRoutingFromUiAsync();

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<DisconnectProfileHandler>().HandleAsync(row.Id));

        if (!result.Success)
        {
            _toast.ShowError(T("Toast_Disconnect_Failed"), result.ErrorMessage);
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
            _toast.ShowError(T("Toast_Delete_Failed"), result.ErrorMessage);
            return;
        }

        Profiles.Remove(row);
        if (SelectedProfile?.Id == row.Id)
            SelectedProfile = null;

        _toast.ShowSuccess(T("Toast_Delete_Success"));
        await RefreshProfilesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanApplySplitRouting))]
    private async Task ApplySplitRoutingAsync()
    {
        if (SelectedProfile is null)
            return;

        if (_splitRoutingApplyInProgress)
            return;

        _splitRoutingApplyInProgress = true;
        IsApplyingRoutes = true;
        ApplyRoutesStatus = T("Progress_Preparing");
        try
        {
            if (!await PersistSplitRoutingFromUiAsync())
                return;

            var progress = new Progress<string>(status =>
            {
                var localized = LocalizationProgress.Format(Localization, status);
                Dispatcher.UIThread.Post(() =>
                {
                    ApplyRoutesStatus = localized;
                    _statusBar.StatusText = localized;
                });
            });

            var profileId = SelectedProfile.Id;
            var result = await _invoker.InvokeAsync(sp =>
                sp.GetRequiredService<ApplySplitRoutingHandler>().HandleAsync(profileId, progress));

            if (!result.Success)
            {
                _toast.ShowError(T("Toast_Routes_Failed"), result.ErrorMessage);
                return;
            }

            CaptureSavedSplitRoutingFromUi();

            if (result.RoutesCsv is null)
            {
                _toast.ShowInfo(T("Toast_Routes_Unchanged"), Tf("Toast_Routes_Unchanged_Detail", result.RouteCount));
                return;
            }

            _toast.ShowSuccess(T("Toast_Routes_Success"), Tf("Toast_Routes_Success_Detail", result.RouteCount));
            if (result.RouteCount >= 200)
                _toast.ShowInfo(T("Toast_Routes_Limit"), T("Toast_Routes_Limit_Detail"));

            if (SelectedProfile is not null)
            {
                SelectedProfile.State = ConnectionState.Connected;
                OnSelectedProfileActionsChanged();
            }
        }
        finally
        {
            _splitRoutingApplyInProgress = false;
            IsApplyingRoutes = false;
            ApplyRoutesStatus = string.Empty;
            ApplySplitRoutingCommand.NotifyCanExecuteChanged();
            await PollProfileStatesAsync();
        }
    }

    partial void OnSplitRoutingEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(SplitRoutingOptionsEnabled));
        OnPropertyChanged(nameof(SelectedSplitRoutingOnOff));
        OnSplitRoutingSettingsEdited();
    }

    partial void OnSplitYoutubeChanged(bool value) => OnSplitRoutingSettingsEdited();

    partial void OnSplitTelegramChanged(bool value) => OnSplitRoutingSettingsEdited();

    partial void OnSplitTwitchChanged(bool value) => OnSplitRoutingSettingsEdited();

    partial void OnSplitCloudflareChanged(bool value) => OnSplitRoutingSettingsEdited();

    partial void OnCustomDomainsTextChanged(string value) => OnSplitRoutingSettingsEdited();

    partial void OnSelectedProfileChanged(ProfileRowViewModel? value)
    {
        OnSelectedProfileActionsChanged();
        OnPropertyChanged(nameof(ShowReconnectHint));
        OnPropertyChanged(nameof(SelectedSplitRoutingOnOff));
        if (value?.Id == _loadedSplitRoutingProfileId)
            return;

        _loadedSplitRoutingProfileId = value?.Id;
        _ = LoadSplitRoutingAsync(value);
    }

    private void OnSelectedProfileActionsChanged()
    {
        OnPropertyChanged(nameof(CanConnectSelected));
        OnPropertyChanged(nameof(CanDisconnectSelected));
        OnPropertyChanged(nameof(ShowReconnectHint));
        ApplySplitRoutingCommand.NotifyCanExecuteChanged();
    }

    private void OnSplitRoutingSettingsEdited()
    {
        if (_loadingSplitRouting)
            return;

        UpdateSplitRoutingDirtyState();
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

    private async Task LoadSplitRoutingAsync(ProfileRowViewModel? row)
    {
        if (row is null)
            return;

        var profile = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<WireguardGui.Application.Abstractions.IProfileStore>()
                .GetProfileAsync(row.Id));

        if (profile is null)
            return;

        _loadingSplitRouting = true;
        try
        {
            SplitRoutingEnabled = profile.SplitRouting.Enabled;
            SplitYoutube = profile.SplitRouting.Youtube;
            SplitTelegram = profile.SplitRouting.Telegram;
            SplitTwitch = profile.SplitRouting.Twitch;
            SplitCloudflare = profile.SplitRouting.IncludeCloudflare;
            CustomDomainsText = string.Join('\n', profile.SplitRouting.CustomDomains);
            _savedSplitRouting = profile.SplitRouting;
            UpdateSplitRoutingDirtyState();
        }
        finally
        {
            _loadingSplitRouting = false;
        }
    }

    private SplitRoutingSettings BuildSplitRoutingSettingsFromUi()
    {
        var domains = CustomDomainsText
            .Split(['\n', '\r', ' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SplitRoutingSettings(
            SplitRoutingEnabled,
            SplitYoutube,
            SplitTelegram,
            SplitTwitch,
            domains,
            SplitCloudflare,
            200);
    }

    private void CaptureSavedSplitRoutingFromUi()
    {
        _savedSplitRouting = BuildSplitRoutingSettingsFromUi();
        UpdateSplitRoutingDirtyState();
    }

    private void UpdateSplitRoutingDirtyState()
    {
        var current = BuildSplitRoutingSettingsFromUi();
        HasUnappliedSplitRoutingChanges = _savedSplitRouting is null
            || !SplitRoutingSettingsEqual(_savedSplitRouting, current);
        OnPropertyChanged(nameof(CanApplySplitRouting));
        ApplySplitRoutingCommand.NotifyCanExecuteChanged();
    }

    private static bool SplitRoutingSettingsEqual(SplitRoutingSettings left, SplitRoutingSettings right) =>
        left.Enabled == right.Enabled
        && left.Youtube == right.Youtube
        && left.Telegram == right.Telegram
        && left.Twitch == right.Twitch
        && left.IncludeCloudflare == right.IncludeCloudflare
        && left.MaxRoutes == right.MaxRoutes
        && left.CustomDomains.SequenceEqual(right.CustomDomains, StringComparer.OrdinalIgnoreCase);

    private async Task<bool> PersistSplitRoutingFromUiAsync(bool showToast = false)
    {
        if (SelectedProfile is null)
            return false;

        var settings = BuildSplitRoutingSettingsFromUi();

        var saveResult = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<SaveProfileSplitRoutingHandler>().HandleAsync(
                SelectedProfile.Id,
                settings));

        if (!saveResult.Success)
        {
            _toast.ShowError(T("Toast_SaveSplit_Failed"), saveResult.ErrorMessage);
            return false;
        }

        SelectedProfile.SplitRoutingEnabled = SplitRoutingEnabled;
        if (showToast)
            _toast.ShowSuccess(T("Toast_Split_Saved"));
        return true;
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
        var selectedId = SelectedProfile?.Id;
        var items = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetProfilesHandler>().HandleAsync());

        for (var i = Profiles.Count - 1; i >= 0; i--)
        {
            if (items.All(it => it.Id != Profiles[i].Id))
            {
                if (Profiles[i].Id == selectedId)
                    SelectedProfile = null;
                Profiles.RemoveAt(i);
            }
        }

        foreach (var item in items)
        {
            var row = Profiles.FirstOrDefault(p => p.Id == item.Id);
            if (row is null)
            {
                Profiles.Add(new ProfileRowViewModel(
                    Localization,
                    item.Id,
                    item.Name,
                    item.ConnectionName,
                    item.Backend,
                    item.State,
                    item.SplitRoutingEnabled));
                continue;
            }

            row.Name = item.Name;
            row.ConnectionName = item.ConnectionName;
            row.Backend = item.Backend;
            row.State = item.State;
            row.SplitRoutingEnabled = item.SplitRoutingEnabled;
        }

        for (var target = 0; target < items.Count; target++)
        {
            var id = items[target].Id;
            var current = -1;
            for (var i = 0; i < Profiles.Count; i++)
            {
                if (Profiles[i].Id != id)
                    continue;
                current = i;
                break;
            }

            if (current >= 0 && current != target)
                Profiles.Move(current, target);
        }

        if (selectedId is not null)
        {
            var row = Profiles.FirstOrDefault(p => p.Id == selectedId);
            if (row is not null && !ReferenceEquals(SelectedProfile, row))
                SelectedProfile = row;
        }

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
            nameof(SplitRoutingLabel),
            nameof(SplitEnableLabel),
            nameof(SplitYoutubeLabel),
            nameof(SplitTelegramLabel),
            nameof(SplitTwitchLabel),
            nameof(SplitCloudflareLabel),
            nameof(SplitDomainsLabel),
            nameof(SplitApplyLabel),
            nameof(SplitHintCloudflare),
            nameof(SplitHintTwitch),
            nameof(SplitHintReconnect),
            nameof(SplitSyncingLabel),
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
