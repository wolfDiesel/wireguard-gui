namespace WireguardGui.Domain;

public sealed record SplitRoutingSettings(
    bool Enabled,
    bool Youtube,
    bool Telegram,
    bool Twitch,
    IReadOnlyList<string> CustomDomains,
    bool IncludeCloudflare,
    int MaxRoutes)
{
    public const int DefaultMaxRoutes = 200;
    public const int MinMaxRoutes = 1;
    public const int MaxMaxRoutes = 200;

    public static SplitRoutingSettings CreateDefault() =>
        new(
            Enabled: false,
            Youtube: true,
            Telegram: true,
            Twitch: false,
            CustomDomains: [],
            IncludeCloudflare: false,
            MaxRoutes: DefaultMaxRoutes);

    public SplitRoutingSettings Normalize()
    {
        var maxRoutes = MaxRoutes <= 0 ? DefaultMaxRoutes : Math.Min(MaxRoutes, MaxMaxRoutes);
        var domains = CustomDomains
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return this with
        {
            MaxRoutes = maxRoutes,
            CustomDomains = domains,
        };
    }

    public bool HasAnySourceEnabled =>
        Youtube || Telegram || Twitch || IncludeCloudflare || CustomDomains.Count > 0;
}
