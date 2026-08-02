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
        "usher.ttvnw.net",
        "gql.twitch.tv",
        "vod-secure.twitch.tv",
        "static.twitchcdn.net",
        "www.twitch.tv",
        "assets.twitch.tv",
        "pubsub-edge.twitch.tv",
        "jtvnw.twitchcdn.net",
        "eun1.playlist.ttvnw.net",
        "eun2.playlist.ttvnw.net",
        "eun11.playlist.ttvnw.net",
        "eun13.playlist.ttvnw.net",
        "euw1.playlist.ttvnw.net",
        "euw2.playlist.ttvnw.net",
        "euw3.playlist.ttvnw.net",
        "euw4.playlist.ttvnw.net",
        "*.abs.hls.ttvnw.net",
        "*.j.cloudfront.hls.ttvnw.net",
        "*.live-video.net",
    ];

    public static readonly IReadOnlySet<string> TwitchNonResolvableParents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "abs.hls.ttvnw.net",
            "j.cloudfront.hls.ttvnw.net",
            "live-video.net",
            "ttvnw.net",
            "jtvnw.net",
        };
}
