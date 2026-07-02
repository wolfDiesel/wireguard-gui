namespace WireguardGui.Domain;

public static class SplitRoutingConstants
{
    public const string GoogleJsonUrl = "https://www.gstatic.com/ipranges/goog.json";

    public static readonly IReadOnlyList<string> TelegramRoutes =
    [
        "149.154.160.0/20",
        "91.108.4.0/22",
        "91.108.8.0/22",
        "91.108.16.0/22",
        "91.108.56.0/22",
        "91.105.192.0/23",
        "95.161.64.0/20",
        "185.76.151.0/24",
    ];

    public static readonly IReadOnlyList<string> CloudflareRoutes =
    [
        "188.114.96.0/20",
        "104.16.0.0/12",
        "172.64.0.0/13",
    ];

    public static readonly IReadOnlyList<string> TwitchDomains =
    [
        "twitch.tv",
        "www.twitch.tv",
        "ttvnw.net",
        "jtvnw.net",
        "twitchcdn.net",
        "live-video.net",
        "ext-twitch.tv",
        "passport.twitch.tv",
        "gql.twitch.tv",
        "id.twitch.tv",
        "usher.ttvnw.net",
        "vod-secure.twitch.tv",
        "d1m7jfoe9zdc1j.cloudfront.net",
        "abs.hls.ttvnw.net",
    ];
}
