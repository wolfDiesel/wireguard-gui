using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class SplitRouteBuilder(
    IEnumerable<ISplitRouteSource> sources,
    ILogger<SplitRouteBuilder> logger) : ISplitRouteBuilder
{
    public async Task<IReadOnlyList<string>> BuildRoutesAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Scanning routes: YouTube={Youtube}, Telegram={Telegram}, Twitch={Twitch}, Cloudflare={Cloudflare}, customDomains={Domains}",
            settings.Youtube,
            settings.Telegram,
            settings.Twitch,
            settings.IncludeCloudflare,
            settings.CustomDomains.Count);

        var orderedSources = sources.OrderBy(s => s.Priority).ToList();
        var sourceResults = await Task.WhenAll(
            orderedSources
                .Where(s => s.IsEnabled(settings))
                .Select(s => CollectFromSourceAsync(s, settings, progress, cancellationToken)));

        var maxRoutes = settings.MaxRoutes > 0 ? settings.MaxRoutes : SplitRoutingSettings.DefaultMaxRoutes;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totalCollected = 0;

        foreach (var item in sourceResults.OrderBy(r => r.Priority))
        {
            totalCollected += item.Routes.Count;
            foreach (var route in item.Routes.OrderBy(r => r, StringComparer.Ordinal))
            {
                if (seen.Add(route))
                    result.Add(route);
            }
        }

        if (result.Count > maxRoutes)
        {
            var dropped = result.Count - maxRoutes;
            logger.LogWarning(
                "Routes {Total}, limit {Max} — truncated to {Taken} ({Dropped} dropped)",
                result.Count,
                maxRoutes,
                maxRoutes,
                dropped);
            result = result.Take(maxRoutes).ToList();
        }

        logger.LogInformation("Total routes after scan: {Count} (collected {Raw})", result.Count, totalCollected);
        return result;
    }

    private static async Task<(int Priority, string SourceName, IReadOnlyList<string> Routes)> CollectFromSourceAsync(
        ISplitRouteSource source,
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var routes = await source.CollectAsync(settings, progress, cancellationToken);
        return (source.Priority, source.GetType().Name, routes);
    }
}
