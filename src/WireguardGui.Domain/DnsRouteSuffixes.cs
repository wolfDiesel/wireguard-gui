namespace WireguardGui.Domain;

public static class DnsRouteSuffixes
{
    public static readonly IReadOnlyList<string> Twitch =
    [
        "ttvnw.net",
        "twitch.tv",
        "twitchcdn.net",
        "live-video.net",
        "jtvnw.net",
    ];

    public static readonly IReadOnlyList<string> YouTube =
    [
        "googlevideo.com",
        "ytimg.com",
        "youtube.com",
        "youtu.be",
        "ggpht.com",
        "gvt1.com",
        "youtube-nocookie.com",
        "yt.be",
    ];

    public static IReadOnlyList<string> FromSettings(SplitRoutingSettings settings)
    {
        if (!settings.Enabled)
            return [];

        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (settings.Twitch)
        {
            foreach (var suffix in Twitch)
                suffixes.Add(suffix);
        }

        if (settings.Youtube)
        {
            foreach (var suffix in YouTube)
                suffixes.Add(suffix);
        }

        foreach (var domain in settings.CustomDomains)
        {
            var suffix = NormalizeCustom(domain);
            if (suffix is not null)
                suffixes.Add(suffix);
        }

        return suffixes.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool Matches(string host, IReadOnlyList<string> suffixes)
    {
        if (string.IsNullOrWhiteSpace(host) || suffixes.Count == 0)
            return false;

        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.Length == 0)
            return false;

        foreach (var suffix in suffixes)
        {
            if (normalized.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeCustom(string domain)
    {
        var trimmed = domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (trimmed.Length == 0 || trimmed.Contains('/'))
            return null;

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
            trimmed = trimmed[2..];

        return trimmed.Length == 0 ? null : trimmed;
    }
}
