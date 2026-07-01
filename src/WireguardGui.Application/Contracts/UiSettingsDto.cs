using WireguardGui.Domain;

namespace WireguardGui.Application.Contracts;

public sealed record UiSettingsDto(
    int WindowWidth = 960,
    int WindowHeight = 640,
    string ColorScheme = UiColorSchemes.Default,
    string Appearance = UiAppearances.Default,
    string Language = UiLanguages.Default,
    bool TrayEnabled = true,
    bool MinimizeToTray = false,
    bool CloseToTray = true);
