using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class TwitchSplitRouteSource(
    DomainDnsResolver dnsResolver,
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

        var domains = TwitchDomainNormalizer.Normalize(SplitRoutingConstants.TwitchDomains);
        var routes = new List<string>();

        foreach (var domain in domains)
        {
            progress?.Report(new SplitRoutingProgress("Progress_Resolve_Domain", domain));
            logger.LogInformation("Twitch: resolving {Domain}…", domain);

            var ips = await dnsResolver.ResolveIpv4Async(domain, cancellationToken);
            foreach (var ip in ips)
                routes.Add($"{ip}/32");

            logger.LogInformation("Twitch {Domain}: {Count} addresses", domain, ips.Count);
        }

        return routes;
    }
}
