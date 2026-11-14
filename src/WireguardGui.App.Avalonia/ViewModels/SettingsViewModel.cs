using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.Services;
using WireguardGui.App.Avalonia.Theme;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.ViewModels;

internal sealed partial class LanguageOptionViewModel : ViewModelBase
{
    public LanguageOptionViewModel(string code, string label)
    {
        Code = code;
        Label = label;
    }

    public string Code { get; }

    [ObservableProperty]
    private string _label;
}

internal sealed partial class PaletteChipViewModel(string id, string label, string primaryHex) : ViewModelBase
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string PrimaryHex { get; } = primaryHex;
}

internal sealed partial class SettingsViewModel : LocalizedViewModelBase
{
    private readonly HandlerInvoker _invoker;
    private readonly ThemeService _themeService;
    private readonly DesktopSessionBridge _desktopSession;
    private readonly ISplitRoutingRefreshScheduler _refreshScheduler;
    private readonly AppToastService _toast;

    [ObservableProperty]
    private string _selectedAppearance = UiAppearances.Dark;

    [ObservableProperty]
    private string _selectedColorScheme = UiColorSchemes.Default;

    [ObservableProperty]
    private string _selectedLanguage = UiLanguages.Default;

    [ObservableProperty]
    private bool _trayEnabled = true;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private decimal _splitRoutingRefreshMinutes = AppSettings.DefaultRefreshMinutes;

    [ObservableProperty]
    private bool _amnesiaTabSelected;

    [ObservableProperty]
    private bool _amneziaWgBusy;

    [ObservableProperty]
    private bool _amneziaWgStatusLoaded;

    [ObservableProperty]
    private AmneziaWgStatusDto? _amneziaWgStatus;

    public ObservableCollection<LanguageOptionViewModel> Languages { get; } = new();
    public ObservableCollection<PaletteChipViewModel> ColorSchemes { get; } = new(
        AccentPalettes.All.Select(p => new PaletteChipViewModel(p.Id, p.Id, p.Primary)));

    public string Title => T("Settings_Title");
    public string ThemeLabel => T("Settings_Theme");
    public string AppearanceDark => T("Settings_Appearance_Dark");
    public string AppearanceLight => T("Settings_Appearance_Light");
    public string AppearanceSystem => T("Settings_Appearance_System");
    public string AccentLabel => T("Settings_Accent");
    public string TrayLabel => T("Settings_Tray");
    public string TrayEnableLabel => T("Settings_Tray_Enable");
    public string TrayMinimizeLabel => T("Settings_Tray_Minimize");
    public string TrayCloseLabel => T("Settings_Tray_Close");
    public string LanguageLabel => T("Settings_Language");
    public string SplitRoutingLabel => T("Settings_SplitRouting");
    public string SplitRoutingRefreshLabel => T("Settings_SplitRouting_Refresh");
    public string SaveLabel => T("Settings_Save");

    public string TabGeneralLabel => T("Settings_Tab_General");
    public string TabAmnesiaLabel => T("Settings_Tab_Amnesia");
    public string AmneziaWgTitle => T("AmneziaWg_Title");
    public string AmneziaWgHint => T("AmneziaWg_Hint");
    public string AmneziaWgVersionLabel => T("AmneziaWg_Version");
    public string AmneziaWgTransportLabel => T("AmneziaWg_Transport");
    public string AmneziaWgCheckLabel => T("AmneziaWg_Check");
    public string AmneziaWgInstallLabel => T("AmneziaWg_Install");
    public string AmneziaWgManualLabel => T("AmneziaWg_ManualInstall");

    public bool IsGeneralTab => !AmnesiaTabSelected;

    public bool IsAmneziaWgAvailable => AmneziaWgStatus?.IsAvailable == true;

    public bool ShowInstallControls => AmneziaWgStatusLoaded && !IsAmneziaWgAvailable;

    public bool ShowAmneziaWgMissing => AmneziaWgStatus?.MissingComponents.Count > 0;

    public string AmneziaWgStatusText => AmneziaWgStatus is null
        ? string.Empty
        : IsAmneziaWgAvailable
            ? T("AmneziaWg_Available")
            : T("AmneziaWg_NotInstalled");

    public string AmneziaWgMissingText => ShowAmneziaWgMissing
        ? Tf("AmneziaWg_Missing", string.Join(", ", AmneziaWgStatus!.MissingComponents))
        : string.Empty;

    public string AmneziaWgToolsText => AmneziaWgStatus?.ToolsVersion is { Length: > 0 } version
        ? version
        : T("AmneziaWg_VersionUnknown");

    public string AmneziaWgTransportText => AmneziaWgStatus switch
    {
        null => string.Empty,
        { Transport: AmneziaWgTransport.KernelModule } status => Tf(
            "AmneziaWg_Transport_Kernel",
            status.TransportVersion ?? T("AmneziaWg_VersionUnknown")),
        { Transport: AmneziaWgTransport.Userspace } => T("AmneziaWg_Transport_Userspace"),
        _ => T("AmneziaWg_Transport_None"),
    };

    public SettingsViewModel(
        HandlerInvoker invoker,
        ThemeService themeService,
        DesktopSessionBridge desktopSession,
        ISplitRoutingRefreshScheduler refreshScheduler,
        AppToastService toast,
        LocalizationService localization)
        : base(localization)
    {
        _invoker = invoker;
        _themeService = themeService;
        _desktopSession = desktopSession;
        _refreshScheduler = refreshScheduler;
        _toast = toast;
        RebuildLanguages();
    }

    public async Task InitializeAsync()
    {
        var settings = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetSettingsHandler>().HandleAsync());

        SelectedAppearance = settings.Ui.Appearance;
        SelectedColorScheme = settings.Ui.ColorScheme;
        SelectedLanguage = settings.Ui.Language;
        TrayEnabled = settings.Ui.TrayEnabled;
        MinimizeToTray = settings.Ui.MinimizeToTray;
        CloseToTray = settings.Ui.CloseToTray;
        SplitRoutingRefreshMinutes = settings.SplitRoutingRefreshMinutes;
    }

    [RelayCommand]
    private void SelectAppearance(string appearance)
    {
        SelectedAppearance = appearance;
        ApplyPreview();
    }

    [RelayCommand]
    private void SelectColorScheme(string scheme)
    {
        SelectedColorScheme = scheme;
        ApplyPreview();
    }

    [RelayCommand]
    private void SelectLanguage(string language)
    {
        SelectedLanguage = language;
        Localization.SetLanguage(language);
        ApplyPreview();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var current = await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<GetSettingsHandler>().HandleAsync());

        var updated = current with
        {
            Ui = current.Ui with
            {
                Appearance = SelectedAppearance,
                ColorScheme = SelectedColorScheme,
                Language = SelectedLanguage,
                TrayEnabled = TrayEnabled,
                MinimizeToTray = MinimizeToTray,
                CloseToTray = CloseToTray,
            },
            SplitRoutingRefreshMinutes = decimal.ToInt32(SplitRoutingRefreshMinutes),
        };

        await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<SaveSettingsHandler>().HandleAsync(updated));

        var clamped = AppSettings.ClampRefreshMinutes(decimal.ToInt32(SplitRoutingRefreshMinutes));
        _refreshScheduler.ApplyRefreshInterval(clamped);
        SplitRoutingRefreshMinutes = clamped;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Localization.SetLanguage(SelectedLanguage);
            _desktopSession.UpdateTrayLabels();
            ApplyPreview();
        });
    }

    [RelayCommand]
    private async Task SelectTabAsync(string tab)
    {
        AmnesiaTabSelected = string.Equals(tab, "amnesia", StringComparison.Ordinal);
        if (AmnesiaTabSelected && !AmneziaWgStatusLoaded)
            await RefreshAmneziaWgStatusAsync();
    }

    [RelayCommand]
    private Task CheckAmneziaWgAsync() => RefreshAmneziaWgStatusAsync();

    public async Task InstallAmneziaWgAsync(Control owner)
    {
        var window = owner as Window ?? TopLevel.GetTopLevel(owner) as Window;
        if (window is null)
            return;

        AmneziaWgBusy = true;
        try
        {
            var dialog = new Views.BackendInstallDialog(
                Localization,
                (progress, cancellationToken) => _invoker.InvokeAsync(sp => sp
                    .GetRequiredService<InstallAmneziaWgHandler>()
                    .HandleAsync(progress, cancellationToken)));

            var result = await dialog.ShowDialog<BackendInstallResultDto?>(window);

            if (dialog.ManualRequested)
            {
                await ShowManualInstallAsync(owner);
                return;
            }

            if (result is null)
                return;

            if (result.Success)
            {
                _toast.ShowSuccess(T("AmneziaWg_Install_Done"));
                await RefreshAmneziaWgStatusAsync();
                return;
            }

            var message = result.NeedsAdmin
                ? T("AmneziaWg_Install_NeedsRoot")
                : result.ErrorMessage ?? T("AmneziaWg_Install_Failed");
            _toast.ShowError(T("AmneziaWg_Install_Failed"), message);
        }
        finally
        {
            AmneziaWgBusy = false;
        }
    }

    public async Task ShowManualInstallAsync(Control owner)
    {
        var window = owner as Window ?? TopLevel.GetTopLevel(owner) as Window;
        if (window is null)
            return;

        var dialog = new Views.ManualInstallDialog(Localization);
        await dialog.ShowDialog(window);
    }

    private async Task RefreshAmneziaWgStatusAsync()
    {
        AmneziaWgBusy = true;
        try
        {
            var status = await _invoker.InvokeAsync(sp =>
                sp.GetRequiredService<GetAmneziaWgStatusHandler>().HandleAsync());
            AmneziaWgStatus = status;
            AmneziaWgStatusLoaded = true;
        }
        finally
        {
            AmneziaWgBusy = false;
        }
    }

    partial void OnAmnesiaTabSelectedChanged(bool value) =>
        OnPropertyChanged(nameof(IsGeneralTab));

    partial void OnAmneziaWgStatusChanged(AmneziaWgStatusDto? value) =>
        NotifyAmneziaWgChanged();

    partial void OnAmneziaWgStatusLoadedChanged(bool value) =>
        NotifyAmneziaWgChanged();

    private void NotifyAmneziaWgChanged() =>
        NotifyLocalized(
            nameof(AmneziaWgStatusText),
            nameof(AmneziaWgMissingText),
            nameof(AmneziaWgToolsText),
            nameof(AmneziaWgTransportText),
            nameof(IsAmneziaWgAvailable),
            nameof(ShowInstallControls),
            nameof(ShowAmneziaWgMissing));

    private void ApplyPreview() =>
        _themeService.Apply(new UiSettingsDto(
            ColorScheme: SelectedColorScheme,
            Appearance: SelectedAppearance,
            Language: SelectedLanguage,
            TrayEnabled: TrayEnabled,
            MinimizeToTray: MinimizeToTray,
            CloseToTray: CloseToTray));

    protected override void OnLocalizationChanged()
    {
        RebuildLanguages();
        NotifyLocalized(
            nameof(Title),
            nameof(ThemeLabel),
            nameof(AppearanceDark),
            nameof(AppearanceLight),
            nameof(AppearanceSystem),
            nameof(AccentLabel),
            nameof(TrayLabel),
            nameof(TrayEnableLabel),
            nameof(TrayMinimizeLabel),
            nameof(TrayCloseLabel),
            nameof(LanguageLabel),
            nameof(SplitRoutingLabel),
            nameof(SplitRoutingRefreshLabel),
            nameof(SaveLabel),
            nameof(TabGeneralLabel),
            nameof(TabAmnesiaLabel),
            nameof(AmneziaWgTitle),
            nameof(AmneziaWgHint),
            nameof(AmneziaWgVersionLabel),
            nameof(AmneziaWgTransportLabel),
            nameof(AmneziaWgCheckLabel),
            nameof(AmneziaWgInstallLabel),
            nameof(AmneziaWgManualLabel),
            nameof(AmneziaWgStatusText),
            nameof(AmneziaWgMissingText),
            nameof(AmneziaWgToolsText),
            nameof(AmneziaWgTransportText));
    }

    private void RebuildLanguages()
    {
        if (Languages.Count == 0)
        {
            foreach (var code in UiLanguages.All)
                Languages.Add(new LanguageOptionViewModel(code, Localization.GetLanguageLabel(code)));
            return;
        }

        foreach (var language in Languages)
            language.Label = Localization.GetLanguageLabel(language.Code);
    }
}