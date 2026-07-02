using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class CustomDomainsSplitRouteSource(
    DomainDnsResolver dnsResolver,
    ILogger<CustomDomainsSplitRouteSource> logger) : ISplitRouteSource
{
    public int Priority => 1;

    public bool IsEnabled(SplitRoutingSettings settings) => settings.CustomDomains.Count > 0;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        dnsResolver.EnsureDigAvailable();

        var routes = new List<string>();
        var domains = settings.CustomDomains
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var domain in domains)
        {
            progress?.Report(new SplitRoutingProgress("Progress_Resolve_Domain", domain));
            logger.LogInformation("Resolving domain {Domain}…", domain);

            var ips = await dnsResolver.ResolveIpv4Async(domain, cancellationToken);
            foreach (var ip in ips)
                routes.Add($"{ip}/32");

            logger.LogInformation("Domain {Domain}: {Count} addresses", domain, ips.Count);
        }

        return routes;
    }
}
