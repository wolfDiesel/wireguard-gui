using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class TwitchSplitRouteSource(
    DomainDnsResolver dnsResolver,
    ILogger<TwitchSplitRouteSource> logger) : ISplitRouteSource
{
    public string ProgressKey => "Progress_Routes_Twitch";

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Twitch;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(ProgressKey);
        dnsResolver.EnsureDigAvailable();

        var domains = TwitchDomainNormalizer.Normalize(SplitRoutingConstants.TwitchDomains);
        var routes = new List<string>();

        foreach (var domain in domains)
        {
            progress?.Report($"Progress_Resolve_Domain|{domain}");
            logger.LogInformation("Twitch: resolving {Domain}…", domain);

            var ips = await dnsResolver.ResolveIpv4Async(domain, cancellationToken);
            foreach (var ip in ips)
                routes.Add($"{ip}/32");

            logger.LogInformation("Twitch {Domain}: {Count} addresses", domain, ips.Count);
        }

        return routes;
    }
}
