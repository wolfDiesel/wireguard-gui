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

internal sealed partial class SplitRoutingPanelViewModel : LocalizedViewModelBase
{
    private readonly HandlerInvoker _invoker;
    private readonly AppToastService _toast;
    private readonly StatusBarService _statusBar;
    private bool _applyInProgress;
    private bool _loading;
    private string? _loadedProfileId;
    private SplitRoutingSettings? _saved;
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

    [ObservableProperty]
    private bool _hasUnappliedChanges;

    [ObservableProperty]
    private bool _isApplyingRoutes;

    [ObservableProperty]
    private string _applyRoutesStatus = string.Empty;

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
    public string SplitHintDnsRemoved => T("Profiles_Split_Hint_DnsRemoved");
    public string SplitHintReconnect => T("Profiles_Split_Hint_Reconnect");

    public bool CanApplySplitRouting =>
        _selectedProfile is not null && SplitRoutingEnabled && HasUnappliedChanges && !IsApplyingRoutes;

    public bool ShowReconnectHint =>
        _selectedProfile is { IsConnected: true } && SplitRoutingEnabled;

    public SplitRoutingPanelViewModel(
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

    public void BindSelectedProfile(ProfileRowViewModel? profile)
    {
        _selectedProfile = profile;
        OnPropertyChanged(nameof(CanApplySplitRouting));
        OnPropertyChanged(nameof(ShowReconnectHint));
        if (profile?.Id == _loadedProfileId)
            return;

        _loadedProfileId = profile?.Id;
        _ = LoadAsync(profile);
    }

    public async Task PersistBeforeConnectAsync()
    {
        if (_selectedProfile is not null)
            await PersistFromUiAsync();
    }

    [RelayCommand(CanExecute = nameof(CanApplySplitRouting))]
    private async Task ApplyAsync()
    {
        if (_selectedProfile is null || _applyInProgress)
            return;

        _applyInProgress = true;
        IsApplyingRoutes = true;
        ApplyRoutesStatus = T("Progress_Preparing");
        try
        {
            if (!await PersistFromUiAsync())
                return;

            var progress = new Progress<SplitRoutingProgress>(status =>
            {
                var localized = LocalizationProgress.Format(Localization, status);
                Dispatcher.UIThread.Post(() =>
                {
                    ApplyRoutesStatus = localized;
                    _statusBar.StatusText = localized;
                });
            });

            var profileId = _selectedProfile.Id;
            var result = await _invoker.InvokeAsync(sp =>
                sp.GetRequiredService<ApplySplitRoutingHandler>().HandleAsync(profileId, progress));

            if (!result.Success)
            {
                _toast.ShowError(T("Toast_Routes_Failed"), result.ErrorMessage);
                return;
            }

            CaptureSavedFromUi();

            if (result.RoutesCsv is null)
            {
                _toast.ShowInfo(T("Toast_Routes_Unchanged"), Tf("Toast_Routes_Unchanged_Detail", result.RouteCount));
                return;
            }

            _toast.ShowSuccess(T("Toast_Routes_Success"), Tf("Toast_Routes_Success_Detail", result.RouteCount));
            if (result.RouteCount >= SplitRoutingSettings.DefaultMaxRoutes)
                _toast.ShowInfo(T("Toast_Routes_Limit"), T("Toast_Routes_Limit_Detail"));

            if (_selectedProfile is not null)
                _selectedProfile.State = ConnectionState.Connected;
        }
        finally
        {
            _applyInProgress = false;
            IsApplyingRoutes = false;
            ApplyRoutesStatus = string.Empty;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSplitRoutingEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(SplitRoutingOptionsEnabled));
        OnEdited();
    }

    partial void OnSplitYoutubeChanged(bool value) => OnEdited();
    partial void OnSplitTelegramChanged(bool value) => OnEdited();
    partial void OnSplitTwitchChanged(bool value) => OnEdited();
    partial void OnSplitCloudflareChanged(bool value) => OnEdited();
    partial void OnCustomDomainsTextChanged(string value) => OnEdited();

    private async Task LoadAsync(ProfileRowViewModel? row)
    {
        if (row is null)
            return;

        var result = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetProfileSplitRoutingHandler>().HandleAsync(row.Id));

        if (!result.Success || result.Settings is null)
            return;

        _loading = true;
        try
        {
            var settings = result.Settings;
            SplitRoutingEnabled = settings.Enabled;
            SplitYoutube = settings.Youtube;
            SplitTelegram = settings.Telegram;
            SplitTwitch = settings.Twitch;
            SplitCloudflare = settings.IncludeCloudflare;
            CustomDomainsText = string.Join('\n', settings.CustomDomains);
            _saved = settings;
            UpdateDirtyState();
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task<bool> PersistFromUiAsync(bool showToast = false)
    {
        if (_selectedProfile is null)
            return false;

        var settings = BuildFromUi();
        var saveResult = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<SaveProfileSplitRoutingHandler>().HandleAsync(
                _selectedProfile.Id,
                settings));

        if (!saveResult.Success)
        {
            var message = OperationErrorMapper.ResolveMessage(Localization, saveResult.ErrorCode, saveResult.ErrorMessage);
            _toast.ShowError(T("Toast_SaveSplit_Failed"), message);
            return false;
        }

        _selectedProfile.SplitRoutingEnabled = SplitRoutingEnabled;
        if (showToast)
            _toast.ShowSuccess(T("Toast_Split_Saved"));
        return true;
    }

    private SplitRoutingSettings BuildFromUi()
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
            SplitRoutingSettings.DefaultMaxRoutes).Normalize();
    }

    private void CaptureSavedFromUi()
    {
        _saved = BuildFromUi();
        UpdateDirtyState();
    }

    private void OnEdited()
    {
        if (_loading)
            return;
        UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        var current = BuildFromUi();
        HasUnappliedChanges = _saved is null || !SettingsEqual(_saved, current);
        OnPropertyChanged(nameof(CanApplySplitRouting));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private static bool SettingsEqual(SplitRoutingSettings left, SplitRoutingSettings right) =>
        left.Enabled == right.Enabled
        && left.Youtube == right.Youtube
        && left.Telegram == right.Telegram
        && left.Twitch == right.Twitch
        && left.IncludeCloudflare == right.IncludeCloudflare
        && left.MaxRoutes == right.MaxRoutes
        && left.CustomDomains.SequenceEqual(right.CustomDomains, StringComparer.OrdinalIgnoreCase);

    protected override void OnLocalizationChanged() =>
        NotifyLocalized(
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
            nameof(SplitHintDnsRemoved),
            nameof(SplitHintReconnect));
}
