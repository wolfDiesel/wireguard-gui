namespace WireguardGui.App.Avalonia.Theme;

internal static class AccentPalettes
{
    internal sealed record Palette(string Id, string Primary, string PrimaryHover);

    public static IReadOnlyList<Palette> All { get; } =
    [
        new("orange", "#F07818", "#E06810"),
        new("teal", "#2EB8AA", "#22A89C"),
        new("blue", "#58A6E8", "#4596D8"),
        new("purple", "#A48AF5", "#9378EB"),
        new("green", "#52C878", "#42B868"),
    ];
}
