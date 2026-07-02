using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class SplitRouteBuilder(
    IEnumerable<ISplitRouteSource> sources,
    ILogger<SplitRouteBuilder> logger) : ISplitRouteBuilder
{
    public async Task<IReadOnlyList<string>> BuildRoutesAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Scanning routes: YouTube={Youtube}, Telegram={Telegram}, Twitch={Twitch}, Cloudflare={Cloudflare}, customDomains={Domains}",
            settings.Youtube,
            settings.Telegram,
            settings.Twitch,
            settings.IncludeCloudflare,
            settings.CustomDomains.Count);

        var routes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            if (!source.IsEnabled(settings))
                continue;

            var collected = await source.CollectAsync(settings, progress, cancellationToken);
            foreach (var route in collected)
                routes.Add(route);
        }

        var maxRoutes = settings.MaxRoutes > 0 ? settings.MaxRoutes : 200;
        var result = routes
            .OrderBy(r => r, StringComparer.Ordinal)
            .Take(maxRoutes)
            .ToList();

        if (result.Count < routes.Count)
            logger.LogWarning(
                "Routes {Total}, limit {Max} — truncated to {Taken}",
                routes.Count,
                maxRoutes,
                result.Count);

        logger.LogInformation("Total routes after scan: {Count}", result.Count);
        return result;
    }
}
