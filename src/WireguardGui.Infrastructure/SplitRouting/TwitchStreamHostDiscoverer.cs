using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WireguardGui.Infrastructure.SplitRouting;

internal sealed partial class TwitchStreamHostDiscoverer(
    HttpClient httpClient,
    ILogger<TwitchStreamHostDiscoverer> logger)
{
    private const string TwitchClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const string GqlUrl = "https://gql.twitch.tv/gql";

    public async Task<IReadOnlyList<string>> DiscoverHostsAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = await FetchPlaybackTokenAsync(channel, cancellationToken).ConfigureAwait(false);
            if (token is null)
                return [];

            var playlist = await FetchUsherPlaylistAsync(channel, token.Value, token.Signature, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(playlist))
                return [];

            var hosts = ExtractHosts(playlist);
            logger.LogInformation(
                "Twitch discovery for {Channel}: {Count} stream hosts",
                channel,
                hosts.Count);
            return hosts;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Twitch stream host discovery failed for {Channel}", channel);
            return [];
        }
    }

    private async Task<PlaybackToken?> FetchPlaybackTokenAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GqlUrl);
        request.Headers.TryAddWithoutValidation("Client-ID", TwitchClientId);
        request.Headers.TryAddWithoutValidation("Device-ID", Guid.NewGuid().ToString("N"));
        request.Content = new StringContent(
            $$"""
            {
              "operationName": "PlaybackAccessToken",
              "variables": {
                "isLive": true,
                "login": "{{channel}}",
                "isVod": false,
                "vodID": "",
                "playerType": "site"
              },
              "extensions": {
                "persistedQuery": {
                  "version": 1,
                  "sha256Hash": "0828119ded1c13477966434e15800ff57ddacf13ba1911c129dc22056918c85b"
                }
              }
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Twitch GQL token request for {Channel} failed: {Status}",
                channel,
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("streamPlaybackAccessToken", out var tokenNode)
            || tokenNode.ValueKind == JsonValueKind.Null)
        {
            logger.LogWarning("Twitch GQL token missing for {Channel} (offline?)", channel);
            return null;
        }

        var value = tokenNode.GetProperty("value").GetString();
        var signature = tokenNode.GetProperty("signature").GetString();
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(signature))
            return null;

        return new PlaybackToken(value, signature);
    }

    private async Task<string?> FetchUsherPlaylistAsync(
        string channel,
        string token,
        string signature,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://usher.ttvnw.net/api/channel/hls/{Uri.EscapeDataString(channel)}.m3u8" +
            $"?client_id={TwitchClientId}" +
            $"&token={Uri.EscapeDataString(token)}" +
            $"&sig={Uri.EscapeDataString(signature)}" +
            "&allow_source=true&allow_audio_only=true&fast_bread=true&playlist_include_framerate=true";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-mpegURL"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Twitch usher playlist for {Channel} failed: {Status}",
                channel,
                (int)response.StatusCode);
            return null;
        }

        var master = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var mediaUrl = ExtractFirstMediaPlaylistUrl(master);
        if (mediaUrl is null)
            return master;

        try
        {
            return await httpClient.GetStringAsync(mediaUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Twitch media playlist fetch failed for {Channel}", channel);
            return master;
        }
    }

    internal static IReadOnlyList<string> ExtractHosts(string playlistOrHtml)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UrlPattern().Matches(playlistOrHtml))
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                continue;
            if (uri.Host.Length == 0)
                continue;
            hosts.Add(uri.Host.ToLowerInvariant());
        }

        return hosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ExtractFirstMediaPlaylistUrl(string masterPlaylist)
    {
        foreach (var line in masterPlaylist.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#') || !line.Contains("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out _))
                return line;
        }

        return null;
    }

    private sealed record PlaybackToken(string Value, string Signature);

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlPattern();
}
