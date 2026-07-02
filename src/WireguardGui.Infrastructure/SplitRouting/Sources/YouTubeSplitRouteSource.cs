using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class YouTubeSplitRouteSource(
    HttpClient httpClient,
    ILogger<YouTubeSplitRouteSource> logger) : ISplitRouteSource
{
    private static readonly string GoogleCachePath = Path.Combine(
        JsonProfileStore.GetDefaultDataRoot(),
        "google-ipranges-cache.json");

    public string ProgressKey => "Progress_Youtube_Download";

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Youtube;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var cached = await LoadGoogleCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cached.Count > 0)
        {
            progress?.Report($"Progress_Youtube_Cache|{cached.Count}");
            logger.LogInformation("Google/YouTube: {Count} routes from cache", cached.Count);
            return cached;
        }

        progress?.Report(ProgressKey);
        logger.LogInformation("Fetching Google/YouTube IP ranges…");

        var routes = await FetchGoogleRoutesOnlineAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Google/YouTube: {Count} routes", routes.Count);
        return routes;
    }

    private async Task<IReadOnlyList<string>> FetchGoogleRoutesOnlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<GoogleIpRangesResponse>(
                SplitRoutingConstants.GoogleJsonUrl,
                cancellationToken);

            var routes = response?.Prefixes?
                .Where(p => !string.IsNullOrWhiteSpace(p.Ipv4Prefix))
                .Select(p => p.Ipv4Prefix!)
                .Distinct()
                .ToList() ?? [];

            if (routes.Count > 0)
                await SaveGoogleCacheAsync(routes, cancellationToken).ConfigureAwait(false);

            return routes;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Google IP ranges online");
            return [];
        }
    }

    private static async Task SaveGoogleCacheAsync(
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GoogleCachePath)!);
        await File.WriteAllTextAsync(
            GoogleCachePath,
            JsonSerializer.Serialize(routes),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> LoadGoogleCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(GoogleCachePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(GoogleCachePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class GoogleIpRangesResponse
    {
        [JsonPropertyName("prefixes")]
        public List<GooglePrefix>? Prefixes { get; set; }
    }

    private sealed class GooglePrefix
    {
        [JsonPropertyName("ipv4Prefix")]
        public string? Ipv4Prefix { get; set; }
    }
}
