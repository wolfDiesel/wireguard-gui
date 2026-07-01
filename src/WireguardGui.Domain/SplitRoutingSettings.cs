namespace WireguardGui.Domain;

public sealed record SplitRoutingSettings(
    bool Enabled,
    bool Youtube,
    bool Telegram,
    IReadOnlyList<string> CustomDomains,
    bool IncludeCloudflare,
    int MaxRoutes)
{
    public static SplitRoutingSettings CreateDefault() =>
        new(
            Enabled: false,
            Youtube: true,
            Telegram: true,
            CustomDomains: [],
            IncludeCloudflare: false,
            MaxRoutes: 200);
}
