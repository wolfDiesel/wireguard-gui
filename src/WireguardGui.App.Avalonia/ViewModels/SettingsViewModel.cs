using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.Services;
using WireguardGui.App.Avalonia.Theme;
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
    public string SaveLabel => T("Settings_Save");

    public SettingsViewModel(
        HandlerInvoker invoker,
        ThemeService themeService,
        DesktopSessionBridge desktopSession,
        LocalizationService localization)
        : base(localization)
    {
        _invoker = invoker;
        _themeService = themeService;
        _desktopSession = desktopSession;
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
        };

        await _invoker.InvokeAsync(sp =>
            sp.GetRequiredService<SaveSettingsHandler>().HandleAsync(updated));

        Localization.SetLanguage(SelectedLanguage);
        _desktopSession.UpdateTrayLabels();
        ApplyPreview();
    }

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
            nameof(SaveLabel));
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