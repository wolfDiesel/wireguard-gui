using Avalonia.Media;

namespace WireguardGui.App.Avalonia.Theme;

internal static class ThemeTokens
{
    public const double IslandRadius = 12;
    public const double ShellGap = 12;

    public static Color MixHex(Color accent, Color baseColor, double accentWeight)
    {
        byte Blend(byte a, byte b) => (byte)Math.Round(a * accentWeight + b * (1 - accentWeight));
        return Color.FromRgb(
            Blend(accent.R, baseColor.R),
            Blend(accent.G, baseColor.G),
            Blend(accent.B, baseColor.B));
    }

    public static (Color Canvas, Color Panel, Color Raised, Color Foreground, Color ForegroundMuted, Color Border, Color BorderMuted)
        Surfaces(bool light) =>
        light
            ? (
                Color.Parse("#FAFAFA"),
                Color.Parse("#FFFFFF"),
                Color.Parse("#F4F4F5"),
                Color.Parse("#18181B"),
                Color.Parse("#52525B"),
                Color.Parse("#1F000000"),
                Color.Parse("#14000000"))
            : (
                Color.Parse("#0A0A0A"),
                Color.Parse("#131313"),
                Color.Parse("#1A1A1A"),
                Color.Parse("#F4F4F5"),
                Color.Parse("#A1A1AA"),
                Color.Parse("#24FFFFFF"),
                Color.Parse("#14FFFFFF"));

    public static BoxShadows IslandShadow(bool light) =>
        light
            ? BoxShadows.Parse("0 4 18 0 #14000000")
            : BoxShadows.Parse("0 6 28 0 #6B000000");
}
