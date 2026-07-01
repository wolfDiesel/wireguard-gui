using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Application.Abstractions;
using WireguardGui.Desktop;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Services;

internal sealed class AvaloniaDesktopSession : IAsyncDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly LocalizationService _localization;
    private readonly Func<Window?> _windowProvider;
    private readonly Func<string?> _activeProfileIdProvider;
    private readonly Func<Task> _connectActiveProfile;
    private readonly Func<Task> _disconnectActiveProfile;

    private AppSettings _settings = null!;
    private ILinuxTrayHost? _tray;
    private bool _quitRequested;

    public AvaloniaDesktopSession(
        ISettingsStore settingsStore,
        LocalizationService localization,
        Func<Window?> windowProvider,
        Func<string?> activeProfileIdProvider,
        Func<Task> connectActiveProfile,
        Func<Task> disconnectActiveProfile)
    {
        _settingsStore = settingsStore;
        _localization = localization;
        _windowProvider = windowProvider;
        _activeProfileIdProvider = activeProfileIdProvider;
        _connectActiveProfile = connectActiveProfile;
        _disconnectActiveProfile = disconnectActiveProfile;
        _localization.Changed += OnLocalizationChanged;
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        if (OperatingSystem.IsLinux())
            GtkDeleteEventHook.Install(ShouldCloseToTray);
        await StartTraySafeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _localization.Changed -= OnLocalizationChanged;
        _tray?.Dispose();
        _tray = null;
        return ValueTask.CompletedTask;
    }

    public void UpdateTrayLabels() => PostToUiThread(UpdateTrayLabelsCore);

    public bool TryCancelClose(Window window)
    {
        if (!ShouldCloseToTray())
            return false;

        window.Hide();
        return true;
    }

    public void ShowMainWindow() => PostToUiThread(ShowMainWindowCore);

    public void RequestQuit() => PostToUiThread(RequestQuitCore);

    private void OnLocalizationChanged(object? sender, EventArgs e) =>
        PostToUiThread(UpdateTrayLabelsCore);

    private void UpdateTrayLabelsCore() => _tray?.UpdateMenuLabels(CreateTrayLabels());

    private TrayMenuLabels CreateTrayLabels() =>
        new(
            _localization.Get("Tray_Show"),
            _localization.Get("Tray_Connect"),
            _localization.Get("Tray_Disconnect"),
            _localization.Get("Tray_Quit"));

    private void ShowMainWindowCore()
    {
        var window = _windowProvider();
        if (window is null)
            return;

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        GtkWindowControl.TryShow();
        DesktopWindowActivator.TryActivate();
    }

    private void RequestQuitCore()
    {
        _quitRequested = true;
        GtkDeleteEventHook.AllowClose();
        _tray?.Dispose();
        _tray = null;

        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static void PostToUiThread(Action action)
    {
        var dispatcher = Dispatcher.UIThread;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }

    private bool ShouldCloseToTray() =>
        OperatingSystem.IsLinux()
        && _settings.Ui.TrayEnabled
        && _settings.Ui.CloseToTray
        && _tray?.IsActive == true
        && !_quitRequested;

    private async Task StartTraySafeAsync()
    {
        if (!OperatingSystem.IsLinux() || !_settings.Ui.TrayEnabled)
            return;

        try
        {
            var tray = LinuxTrayHost.TryCreate(TrayIconPaths.ResolveTrayIconPath(), CreateTrayLabels());
            if (tray is null)
                return;

            tray.ShowRequested += ShowMainWindow;
            tray.QuitRequested += RequestQuit;
            tray.ConnectRequested += () => PostToUiThread(() => _ = _connectActiveProfile());
            tray.DisconnectRequested += () => PostToUiThread(() => _ = _disconnectActiveProfile());
            await tray.StartAsync().ConfigureAwait(false);

            if (!tray.IsActive)
            {
                tray.Dispose();
                return;
            }

            _tray = tray;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"System tray: {ex.Message}");
        }
    }
}
