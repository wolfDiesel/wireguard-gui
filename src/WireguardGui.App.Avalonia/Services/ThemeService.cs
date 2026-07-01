using Avalonia.Media;
using Avalonia.Styling;
using WireguardGui.App.Avalonia.Theme;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Services;

internal sealed class ThemeService
{
    private const double ProgressMixDark = 0.52;
    private const double ProgressMixLight = 0.44;

    private static readonly IReadOnlyDictionary<string, (Color Primary, Color PrimaryHover)> Accents =
        AccentPalettes.All.ToDictionary(
            palette => palette.Id,
            palette => (Color.Parse(palette.Primary), Color.Parse(palette.PrimaryHover)),
            StringComparer.Ordinal);

    public void Apply(UiSettingsDto ui)
    {
        var app = global::Avalonia.Application.Current;
        if (app is null)
            return;

        var appearance = NormalizeAppearance(ui.Appearance);
        var isLight = appearance == UiAppearances.Light;
        app.RequestedThemeVariant = appearance switch
        {
            UiAppearances.Light => ThemeVariant.Light,
            UiAppearances.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        var accent = Accents.TryGetValue(NormalizeColorScheme(ui.ColorScheme), out var palette)
            ? palette
            : Accents[UiColorSchemes.Default];

        var surfaces = ThemeTokens.Surfaces(isLight);
        var rowSurface = isLight ? Color.Parse("#F4F4F5") : Color.Parse("#1A1A1A");
        var rowActiveBase = isLight ? Color.Parse("#E4E4E8") : Color.Parse("#141414");

        SetBrush(app, "AccentPrimaryBrush", accent.Primary);
        SetBrush(app, "AccentPrimaryHoverBrush", accent.PrimaryHover);
        SetBrush(app, "SurfaceCanvasBrush", surfaces.Canvas);
        SetBrush(app, "SurfacePanelBrush", surfaces.Panel);
        SetBrush(app, "SurfaceRaisedBrush", surfaces.Raised);
        SetBrush(app, "ForegroundBrush", surfaces.Foreground);
        SetBrush(app, "ForegroundMutedBrush", surfaces.ForegroundMuted);
        SetBrush(app, "BorderBrush", surfaces.Border);
        SetBrush(app, "BorderMutedBrush", surfaces.BorderMuted);
        SetBrush(app, "RowHoverBrush", ThemeTokens.MixHex(accent.Primary, rowSurface, 0.07));
        SetBrush(app, "RowActiveBrush", ThemeTokens.MixHex(accent.Primary, rowActiveBase, 0.11));

        var progressTrack = isLight ? Color.Parse("#E4E4E8") : Color.Parse("#1A1A1A");
        var progressMix = isLight ? ProgressMixLight : ProgressMixDark;
        SetBrush(app, "ProgressTrackBrush", progressTrack);
        SetBrush(app, "ProgressFillBrush", ThemeTokens.MixHex(accent.Primary, progressTrack, progressMix));

        SetBrush(app, "SuccessBrush", Color.Parse("#22C55E"));
        SetBrush(app, "DangerBrush", Color.Parse("#EF4444"));

        app.Resources["SystemAccentColor"] = accent.Primary;
        app.Resources["SystemAccentColorDark1"] = accent.PrimaryHover;
    }

    private static void SetBrush(global::Avalonia.Application app, string key, Color color) =>
        app.Resources[key] = new SolidColorBrush(color);

    private static string NormalizeColorScheme(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UiColorSchemes.All.Contains(value)
            ? value
            : UiColorSchemes.Default;

    private static string NormalizeAppearance(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UiAppearances.All.Contains(value)
            ? value
            : UiAppearances.Default;
}
