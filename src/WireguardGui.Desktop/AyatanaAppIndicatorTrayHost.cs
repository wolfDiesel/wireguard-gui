using System.Runtime.InteropServices;

namespace WireguardGui.Desktop;

internal sealed class AyatanaAppIndicatorTrayHost : ILinuxTrayHost
{
    private const int StatusActive = 2;
    private const int StatusPassive = 1;
    private const int CategoryApplicationStatus = 0;
    private const string IndicatorId = "wireguard-gui";

    private static readonly string[] IndicatorLibraries =
    [
        "libayatana-appindicator3.so.1",
        "libappindicator3.so.1",
    ];

    private static bool s_initialized;
    private static IntPtr s_indicatorLib;
    private static app_indicator_new_delegate? s_appIndicatorNew;
    private static app_indicator_new_with_path_delegate? s_appIndicatorNewWithPath;
    private static app_indicator_set_status_delegate? s_appIndicatorSetStatus;
    private static app_indicator_set_icon_theme_path_delegate? s_appIndicatorSetIconThemePath;
    private static app_indicator_set_icon_full_delegate? s_appIndicatorSetIconFull;
    private static app_indicator_set_menu_delegate? s_appIndicatorSetMenu;

    private readonly string? _iconPath;
    private TrayMenuLabels _labels;
    private IntPtr _indicator;
    private GtkTrayMenu? _menu;
    private GtkTrayEventLoop? _eventLoop;
    private bool _disposed;

    private AyatanaAppIndicatorTrayHost(string? iconPath, TrayMenuLabels labels)
    {
        _iconPath = iconPath;
        _labels = labels;
    }

    public bool IsActive => _indicator != IntPtr.Zero;

    public event Action? ShowRequested;
    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? QuitRequested;

    public static ILinuxTrayHost? TryCreate(string? iconPath, TrayMenuLabels labels)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            EnsureIndicatorLibrary();
            return new AyatanaAppIndicatorTrayHost(iconPath, labels);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"System tray (AppIndicator): library unavailable ({ex.Message}).");
            return null;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!GtkBootstrap.EnsureDisplay())
        {
            Console.Error.WriteLine("System tray (AppIndicator): GTK display is not available.");
            return Task.CompletedTask;
        }

        try
        {
            _menu = GtkTrayMenu.TryCreate(
                _labels,
                () => ShowRequested?.Invoke(),
                () => QuitRequested?.Invoke(),
                () => ConnectRequested?.Invoke(),
                () => DisconnectRequested?.Invoke());
            if (_menu is null)
                throw new InvalidOperationException("GTK tray menu was not created.");

            var iconFile = ResolveIconFilePath();
            var themeIcon = TrayIconThemeInstall.TryInstall(iconFile) ?? TrayIconThemeInstall.IconName;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var hicolorRoot = Path.Combine(home, ".local/share/icons/hicolor");

            _indicator = CreateIndicator(themeIcon, hicolorRoot);
            if (_indicator == IntPtr.Zero)
                throw new InvalidOperationException("app_indicator_new returned null.");

            if (Directory.Exists(hicolorRoot))
                s_appIndicatorSetIconThemePath!(_indicator, hicolorRoot);

            ApplyIcon(_indicator, iconFile, themeIcon);
            s_appIndicatorSetMenu!(_indicator, _menu.Handle);
            s_appIndicatorSetStatus!(_indicator, StatusActive);

            _eventLoop = new GtkTrayEventLoop();
            _eventLoop.Start();
            GtkBootstrap.PumpEvents();
        }
        catch (Exception ex)
        {
            _indicator = IntPtr.Zero;
            Console.Error.WriteLine($"System tray (AppIndicator): {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public void UpdateMenuLabels(TrayMenuLabels labels)
    {
        _labels = labels;
        if (_menu is null)
            return;

        _menu.Dispose();
        _menu = GtkTrayMenu.TryCreate(
            _labels,
            () => ShowRequested?.Invoke(),
            () => QuitRequested?.Invoke(),
            () => ConnectRequested?.Invoke(),
            () => DisconnectRequested?.Invoke());
        if (_menu is not null && _indicator != IntPtr.Zero)
            s_appIndicatorSetMenu!(_indicator, _menu.Handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _eventLoop?.Dispose();

        if (_indicator != IntPtr.Zero)
        {
            try
            {
                s_appIndicatorSetStatus!(_indicator, StatusPassive);
            }
            catch
            {
            }

            _indicator = IntPtr.Zero;
        }

        _menu?.Dispose();
        _menu = null;
    }

    private string? ResolveIconFilePath()
    {
        if (!string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
            return Path.GetFullPath(_iconPath);

        return TrayIconPaths.ResolveTrayIconPath();
    }

    private IntPtr CreateIndicator(string iconName, string hicolorRoot)
    {
        if (s_appIndicatorNewWithPath is not null && Directory.Exists(hicolorRoot))
            return s_appIndicatorNewWithPath(IndicatorId, iconName, CategoryApplicationStatus, hicolorRoot);

        return s_appIndicatorNew!(IndicatorId, iconName, CategoryApplicationStatus);
    }

    private static void ApplyIcon(IntPtr indicator, string? iconFilePath, string iconName)
    {
        if (!string.IsNullOrEmpty(iconFilePath))
        {
            s_appIndicatorSetIconFull!(indicator, iconFilePath, GtkWindowControl.WindowTitle);
            return;
        }

        s_appIndicatorSetIconFull!(indicator, iconName, string.Empty);
    }

    private static void EnsureIndicatorLibrary()
    {
        if (s_initialized)
            return;

        s_indicatorLib = LoadLibrary(IndicatorLibraries);
        if (s_indicatorLib == IntPtr.Zero)
            throw new InvalidOperationException("libayatana-appindicator3 not found.");

        s_appIndicatorNew = GetExport<app_indicator_new_delegate>(s_indicatorLib, "app_indicator_new");
        if (NativeLibrary.TryGetExport(s_indicatorLib, "app_indicator_new_with_path", out _))
        {
            s_appIndicatorNewWithPath = GetExport<app_indicator_new_with_path_delegate>(
                s_indicatorLib,
                "app_indicator_new_with_path");
        }

        s_appIndicatorSetStatus = GetExport<app_indicator_set_status_delegate>(s_indicatorLib, "app_indicator_set_status");
        s_appIndicatorSetIconThemePath = GetExport<app_indicator_set_icon_theme_path_delegate>(
            s_indicatorLib,
            "app_indicator_set_icon_theme_path");
        s_appIndicatorSetIconFull = GetExport<app_indicator_set_icon_full_delegate>(
            s_indicatorLib,
            "app_indicator_set_icon_full");
        s_appIndicatorSetMenu = GetExport<app_indicator_set_menu_delegate>(s_indicatorLib, "app_indicator_set_menu");

        s_initialized = true;
    }

    private static IntPtr LoadLibrary(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (NativeLibrary.TryLoad(name, out var handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    private static T GetExport<T>(IntPtr library, string name)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr app_indicator_new_delegate(
        [MarshalAs(UnmanagedType.LPStr)] string id,
        [MarshalAs(UnmanagedType.LPStr)] string iconName,
        int category);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr app_indicator_new_with_path_delegate(
        [MarshalAs(UnmanagedType.LPStr)] string id,
        [MarshalAs(UnmanagedType.LPStr)] string iconName,
        int category,
        [MarshalAs(UnmanagedType.LPStr)] string iconThemePath);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void app_indicator_set_status_delegate(IntPtr indicator, int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void app_indicator_set_icon_theme_path_delegate(IntPtr indicator, string path);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void app_indicator_set_icon_full_delegate(
        IntPtr indicator,
        [MarshalAs(UnmanagedType.LPStr)] string iconName,
        [MarshalAs(UnmanagedType.LPStr)] string iconDesc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void app_indicator_set_menu_delegate(IntPtr indicator, IntPtr menu);
}
