namespace WireguardGui.Domain;

public sealed record AppSettings(UiSettings Ui, int SplitRoutingRefreshMinutes)
{
    public const int DefaultRefreshMinutes = 10;
    public const int MinRefreshMinutes = 1;
    public const int MaxRefreshMinutes = 120;

    public static AppSettings CreateDefault() =>
        new(UiSettings.CreateDefault(), DefaultRefreshMinutes);

    public AppSettings Normalize() =>
        this with
        {
            SplitRoutingRefreshMinutes = ClampRefreshMinutes(SplitRoutingRefreshMinutes),
        };

    public static int ClampRefreshMinutes(int minutes)
    {
        if (minutes <= 0)
            return DefaultRefreshMinutes;
        return Math.Clamp(minutes, MinRefreshMinutes, MaxRefreshMinutes);
    }
}

public sealed record UiSettings(
    int WindowWidth,
    int WindowHeight,
    string ColorScheme,
    string Appearance,
    string Language,
    bool TrayEnabled,
    bool MinimizeToTray,
    bool CloseToTray)
{
    public static UiSettings CreateDefault() =>
        new(
            WindowWidth: 960,
            WindowHeight: 640,
            ColorScheme: UiColorSchemes.Default,
            Appearance: UiAppearances.Default,
            Language: UiLanguages.Default,
            TrayEnabled: true,
            MinimizeToTray: false,
            CloseToTray: true);
}
