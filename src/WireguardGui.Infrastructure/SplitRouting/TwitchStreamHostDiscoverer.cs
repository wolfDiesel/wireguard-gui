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
    // Публичный Client-ID Twitch-клиента (не секрет). Если Twitch отзовёт его,
    // discovery перестанет работать — запасной путь: курируемые FQDN + кэш.
    private const string TwitchClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";
    private const string GqlUrl = "https://gql.twitch.tv/gql";
    private const string PlaybackAccessTokenHash = "ed230aa1e33e07eebb8928504583da78a5173989fadfb1ac94be06a04f3cdbe9";
    private const int MaxRetries = 2;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

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
        var payload = JsonSerializer.Serialize(new
        {
            operationName = "PlaybackAccessToken",
            variables = new
            {
                isLive = true,
                login = channel,
                isVod = false,
                vodID = "",
                playerType = "embed",
                platform = "site",
            },
            extensions = new
            {
                persistedQuery = new
                {
                    version = 1,
                    sha256Hash = PlaybackAccessTokenHash,
                },
            },
        });

        using var response = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, GqlUrl);
                request.Headers.TryAddWithoutValidation("Client-ID", TwitchClientId);
                request.Headers.TryAddWithoutValidation("Device-ID", Guid.NewGuid().ToString("N"));
                request.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                return request;
            },
            channel,
            "GQL token",
            cancellationToken).ConfigureAwait(false);
        if (response is null)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var msg) ? msg.GetString() : "unknown";
            logger.LogWarning("Twitch GQL token error for {Channel}: {Message}", channel, message);
            return null;
        }

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("streamPlaybackAccessToken", out var tokenNode)
            || tokenNode.ValueKind == JsonValueKind.Null)
        {
            logger.LogInformation("Twitch GQL token missing for {Channel} (channel offline)", channel);
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
        var query = new Dictionary<string, string>
        {
            ["platform"] = "web",
            ["sig"] = signature,
            ["token"] = token,
            ["allow_source"] = "true",
            ["allow_audio_only"] = "true",
            ["fast_bread"] = "true",
            ["p"] = Random.Shared.Next(0, 999999).ToString(),
            ["player_backend"] = "mediaplayer",
            ["playlist_include_framerate"] = "true",
            ["supported_codecs"] = "avc1",
        };
        var url =
            $"https://usher.ttvnw.net/api/v2/channel/hls/{Uri.EscapeDataString(channel.ToLowerInvariant())}.m3u8" +
            $"?{string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))}";

        using var response = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-mpegURL"));
                request.Headers.TryAddWithoutValidation("Client-ID", TwitchClientId);
                request.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                return request;
            },
            channel,
            "usher playlist",
            cancellationToken).ConfigureAwait(false);
        if (response is null)
            return null;

        var master = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var mediaUrls = ExtractMediaPlaylistUrls(master);
        if (mediaUrls.Count == 0)
            return master;

        var combined = new StringBuilder(master);
        foreach (var mediaUrl in mediaUrls.Take(3))
        {
            try
            {
                using var mediaRequest = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
                mediaRequest.Headers.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                using var mediaResponse = await httpClient.SendAsync(mediaRequest, cancellationToken)
                    .ConfigureAwait(false);
                if (!mediaResponse.IsSuccessStatusCode)
                    continue;

                combined.Append('\n');
                combined.Append(await mediaResponse.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Twitch media playlist fetch timed out for {Channel}", channel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Twitch media playlist fetch failed for {Channel}", channel);
            }
        }

        return combined.ToString();
    }

    internal static IReadOnlyList<string> ExtractHosts(string playlistOrHtml)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UrlPattern().Matches(playlistOrHtml))
            TryAddHost(hosts, match.Value);

        foreach (Match match in SessionDataValuePattern().Matches(playlistOrHtml))
        {
            var value = match.Groups[1].Value;
            if (value.Contains("://", StringComparison.Ordinal))
                TryAddHost(hosts, value);
            else if (LooksLikeHostname(value))
                hosts.Add(value.ToLowerInvariant());

            TryAddHostsFromBase64(hosts, value);
        }

        return hosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool LooksLikeHostname(string value) =>
        value.Contains('.', StringComparison.Ordinal) &&
        !value.Contains(' ', StringComparison.Ordinal) &&
        !value.Contains('/', StringComparison.Ordinal) &&
        value.Any(char.IsLetter);

    private static IReadOnlyList<string> ExtractMediaPlaylistUrls(string masterPlaylist)
    {
        var urls = new List<string>();
        foreach (var line in masterPlaylist.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#') || !line.Contains("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out _))
                urls.Add(line);
        }

        return urls;
    }

    private static void TryAddHost(HashSet<string> hosts, string candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return;
        if (uri.Host.Length == 0)
            return;
        hosts.Add(uri.Host.ToLowerInvariant());
    }

    private static void TryAddHostsFromBase64(HashSet<string> hosts, string value)
    {
        if (value.Length < 16 || value.Contains('.', StringComparison.Ordinal))
            return;

        try
        {
            var padded = value;
            var mod = padded.Length % 4;
            if (mod != 0)
                padded += new string('=', 4 - mod);

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            foreach (Match match in UrlPattern().Matches(decoded))
                TryAddHost(hosts, match.Value);
        }
        catch (FormatException)
        {
        }
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        string channel,
        string what,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var request = requestFactory();
                var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return response;

                var status = (int)response.StatusCode;
                if (status is 429 or >= 500)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                    var delay = retryAfter is > 0
                        ? TimeSpan.FromSeconds(retryAfter.Value)
                        : attempt < RetryDelays.Length ? RetryDelays[attempt] : TimeSpan.Zero;
                    logger.LogWarning(
                        "Twitch {What} for {Channel} failed: {Status} (attempt {Attempt}/{MaxRetries}); retrying in {Delay}s",
                        what,
                        channel,
                        status,
                        attempt + 1,
                        MaxRetries + 1,
                        delay.TotalSeconds);
                    response.Dispose();
                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }
                else
                {
                    logger.LogWarning(
                        "Twitch {What} for {Channel} failed: {Status}",
                        what,
                        channel,
                        status);
                }

                response.Dispose();
                return null;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Twitch {What} request for {Channel} timed out", what, channel);
                if (attempt < MaxRetries)
                {
                    var delay = attempt < RetryDelays.Length ? RetryDelays[attempt] : TimeSpan.Zero;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Twitch {What} request for {Channel} failed (network)", what, channel);
                if (attempt < MaxRetries)
                {
                    var delay = attempt < RetryDelays.Length ? RetryDelays[attempt] : TimeSpan.Zero;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return null;
            }
        }

        return null;
    }

    private sealed record PlaybackToken(string Value, string Signature);

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"VALUE=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SessionDataValuePattern();
}
