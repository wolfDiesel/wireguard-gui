using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.Services;
using WireguardGui.App.Avalonia.ViewModels;
using WireguardGui.App.Avalonia.Views;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Desktop;

namespace WireguardGui.App.Avalonia;

public partial class App : global::Avalonia.Application
{
    private AvaloniaDesktopSession? _desktopSession;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var localization = AppServices.GetRequired<LocalizationService>();
        localization.SetUiSynchronizer(action =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Invoke(action);
        });
        ServiceRegistration.WireLocalization(localization);

        var mainVm = AppServices.GetRequired<MainWindowViewModel>();
        var profilesVm = AppServices.GetRequired<ProfilesViewModel>();
        var settingsStore = AppServices.GetRequired<ISettingsStore>();
        var sessionBridge = AppServices.GetRequired<DesktopSessionBridge>();
        var settings = await settingsStore.LoadAsync();
        localization.SetLanguage(settings.Ui.Language);

        AppServices.GetRequired<ThemeService>().Apply(new UiSettingsDto(
            settings.Ui.WindowWidth,
            settings.Ui.WindowHeight,
            settings.Ui.ColorScheme,
            settings.Ui.Appearance,
            settings.Ui.Language,
            settings.Ui.TrayEnabled,
            settings.Ui.MinimizeToTray,
            settings.Ui.CloseToTray));

        var window = new MainWindow
        {
            DataContext = mainVm,
            Width = settings.Ui.WindowWidth,
            Height = settings.Ui.WindowHeight,
            Title = localization.Get("App_Title"),
        };

        localization.Changed += (_, _) => window.Title = localization.Get("App_Title");

        _desktopSession = new AvaloniaDesktopSession(
            settingsStore,
            localization,
            () => window,
            () => profilesVm.GetActiveConnectedProfileId(),
            () => profilesVm.ConnectActiveProfileAsync(),
            () => profilesVm.DisconnectActiveProfileAsync());

        sessionBridge.Attach(_desktopSession);

        await _desktopSession.InitializeAsync();

        window.Closing += (_, e) =>
        {
            if (_desktopSession.TryCancelClose(window))
                e.Cancel = true;
        };

        desktop.MainWindow = window;
        desktop.ShutdownRequested += async (_, _) =>
        {
            profilesVm.StopPolling();
            if (AppServices.GetRequired<IProcessRunner>() is IAsyncDisposable processRunner)
                await processRunner.DisposeAsync();
            if (_desktopSession is not null)
                await _desktopSession.DisposeAsync();
        };

        await mainVm.InitializeAsync();
        window.Show();

        base.OnFrameworkInitializationCompleted();
    }
}
