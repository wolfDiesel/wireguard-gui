namespace WireguardGui.Domain;

public static class UiColorSchemes
{
    public const string Orange = "orange";
    public const string Teal = "teal";
    public const string Blue = "blue";
    public const string Purple = "purple";
    public const string Green = "green";

    public const string Default = Orange;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Orange,
        Teal,
        Blue,
        Purple,
        Green,
    };
}

public static class UiAppearances
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string System = "system";

    public const string Default = Dark;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Light,
        Dark,
        System,
    };
}
