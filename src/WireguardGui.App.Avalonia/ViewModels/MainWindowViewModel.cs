using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.Services;

namespace WireguardGui.App.Avalonia.ViewModels;

internal enum AppPage
{
    Profiles,
    Settings,
    About,
}

internal sealed partial class MainWindowViewModel : LocalizedViewModelBase
{
    private readonly ProfilesViewModel _profiles;
    private readonly SettingsViewModel _settings;
    private readonly AboutViewModel _about;
    private readonly StatusBarService _statusBar;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _navProfilesActive = true;

    [ObservableProperty]
    private bool _navSettingsActive;

    [ObservableProperty]
    private bool _navAboutActive;

    public bool IsConnected => _statusBar.IsConnected;
    public string StatusText => _statusBar.StatusText;
    public string AppTitle => T("App_Title");
    public string NavProfilesLabel => T("Nav_Profiles");
    public string NavSettingsLabel => T("Nav_Settings");
    public string NavAboutLabel => T("Nav_About");

    public MainWindowViewModel(
        ProfilesViewModel profiles,
        SettingsViewModel settings,
        AboutViewModel about,
        StatusBarService statusBar,
        LocalizationService localization)
        : base(localization)
    {
        _profiles = profiles;
        _settings = settings;
        _about = about;
        _statusBar = statusBar;
        _currentPage = profiles;
        _statusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StatusBarService.IsConnected))
                OnPropertyChanged(nameof(IsConnected));
            if (e.PropertyName is nameof(StatusBarService.StatusText))
                OnPropertyChanged(nameof(StatusText));
        };
    }

    [RelayCommand]
    private void NavigateProfiles() => Navigate(AppPage.Profiles);

    [RelayCommand]
    private void NavigateSettings() => Navigate(AppPage.Settings);

    [RelayCommand]
    private void NavigateAbout() => Navigate(AppPage.About);

    private void Navigate(AppPage page)
    {
        CurrentPage = page switch
        {
            AppPage.Settings => _settings,
            AppPage.About => _about,
            _ => _profiles,
        };

        NavProfilesActive = page == AppPage.Profiles;
        NavSettingsActive = page == AppPage.Settings;
        NavAboutActive = page == AppPage.About;
    }

    public async Task InitializeAsync()
    {
        await _profiles.InitializeAsync();
        await _settings.InitializeAsync();
    }

    protected override void OnLocalizationChanged() =>
        NotifyLocalized(nameof(AppTitle), nameof(NavProfilesLabel), nameof(NavSettingsLabel), nameof(NavAboutLabel));
}
