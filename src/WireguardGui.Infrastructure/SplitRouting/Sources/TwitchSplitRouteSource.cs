using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class TwitchSplitRouteSource(
    DomainDnsResolver dnsResolver,
    TwitchStreamHostDiscoverer discoverer,
    TwitchStreamHostCache hostCache,
    ILogger<TwitchSplitRouteSource> logger) : ISplitRouteSource
{
    public int Priority => 1;

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Twitch;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SplitRoutingProgress("Progress_Routes_Twitch"));
        dnsResolver.EnsureDigAvailable();

        var domains = new HashSet<string>(
            TwitchDomainNormalizer.Normalize(SplitRoutingConstants.TwitchDomains),
            StringComparer.OrdinalIgnoreCase);

        var cachedHosts = await hostCache.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var host in cachedHosts)
            domains.Add(host);

        var discovered = 0;
        var channel = settings.TwitchChannel;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            progress?.Report(new SplitRoutingProgress("Progress_Routes_Twitch_Discover", channel));
            logger.LogInformation("Twitch: discovering stream hosts for {Channel}…", channel);
            var hosts = await discoverer.DiscoverHostsAsync(channel, cancellationToken).ConfigureAwait(false);
            discovered = hosts.Count;
            foreach (var host in hosts)
                domains.Add(host);
            if (hosts.Count > 0)
                await hostCache.SaveAsync(hosts.Concat(cachedHosts), cancellationToken).ConfigureAwait(false);
        }

        var routes = new List<string>();
        var resolvedHosts = 0;
        var emptyHosts = 0;

        foreach (var domain in domains.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            progress?.Report(new SplitRoutingProgress("Progress_Resolve_Domain", domain));
            logger.LogInformation("Twitch: resolving {Domain}…", domain);

            var ips = await dnsResolver.ResolveIpv4Async(domain, cancellationToken).ConfigureAwait(false);
            if (ips.Count == 0)
            {
                emptyHosts++;
                logger.LogDebug("Twitch {Domain}: no A records", domain);
                continue;
            }

            resolvedHosts++;
            foreach (var ip in ips)
                routes.Add($"{ip}/32");

            logger.LogInformation("Twitch {Domain}: {Count} addresses", domain, ips.Count);
        }

        logger.LogInformation(
            "Twitch routes: {RouteCount} from {Resolved}/{Total} hosts (discovered={Discovered}, empty={Empty}, cached={Cached})",
            routes.Count,
            resolvedHosts,
            domains.Count,
            discovered,
            emptyHosts,
            cachedHosts.Count);

        return routes;
    }
}
