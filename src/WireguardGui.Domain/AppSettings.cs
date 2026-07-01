namespace WireguardGui.Domain;

public sealed record AppSettings(UiSettings Ui);

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
