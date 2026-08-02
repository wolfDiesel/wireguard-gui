namespace WireguardGui.Domain;

public static class TwitchChannelNaming
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim().TrimStart('@');
        foreach (var prefix in new[]
                 {
                     "https://www.twitch.tv/",
                     "http://www.twitch.tv/",
                     "https://twitch.tv/",
                     "http://twitch.tv/",
                     "www.twitch.tv/",
                     "twitch.tv/",
                 })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        var slash = value.IndexOf('/');
        if (slash >= 0)
            value = value[..slash];

        var query = value.IndexOf('?');
        if (query >= 0)
            value = value[..query];

        value = value.Trim().ToLowerInvariant();
        if (value.Length == 0)
            return null;

        foreach (var ch in value)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-')
                continue;
            return null;
        }

        return value;
    }
}
