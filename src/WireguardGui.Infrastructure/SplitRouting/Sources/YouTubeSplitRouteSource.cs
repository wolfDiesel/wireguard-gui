using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class YouTubeSplitRouteSource(
    HttpClient httpClient,
    IAppDataPaths appDataPaths,
    ILogger<YouTubeSplitRouteSource> logger) : ISplitRouteSource
{
    public int Priority => 2;

    private string GoogleCachePath =>
        Path.Combine(appDataPaths.DataRoot, "google-ipranges-cache.json");

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Youtube;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cached = await LoadGoogleCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cached.Count > 0)
        {
            progress?.Report(new SplitRoutingProgress("Progress_Youtube_Cache", cached.Count.ToString()));
            logger.LogInformation("Google/YouTube: {Count} routes from cache", cached.Count);
            return cached;
        }

        progress?.Report(new SplitRoutingProgress("Progress_Youtube_Download"));
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

    private async Task SaveGoogleCacheAsync(
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(appDataPaths.DataRoot);
        await File.WriteAllTextAsync(
            GoogleCachePath,
            JsonSerializer.Serialize(routes),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> LoadGoogleCacheAsync(CancellationToken cancellationToken)
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
