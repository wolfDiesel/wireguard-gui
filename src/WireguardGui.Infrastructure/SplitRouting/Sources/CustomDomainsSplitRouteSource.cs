using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class CustomDomainsSplitRouteSource(
    DomainDnsResolver dnsResolver,
    ILogger<CustomDomainsSplitRouteSource> logger) : ISplitRouteSource
{
    public string ProgressKey => "Progress_Resolve_Domain";

    public bool IsEnabled(SplitRoutingSettings settings) => settings.CustomDomains.Count > 0;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        dnsResolver.EnsureDigAvailable();

        var routes = new List<string>();

        foreach (var domain in settings.CustomDomains)
        {
            var trimmed = domain.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            progress?.Report($"{ProgressKey}|{trimmed}");
            logger.LogInformation("Resolving domain {Domain}…", trimmed);

            var ips = await dnsResolver.ResolveIpv4Async(trimmed, cancellationToken);
            foreach (var ip in ips)
                routes.Add($"{ip}/32");

            logger.LogInformation(
                "Domain {Domain}: {Count} addresses",
                trimmed,
                ips.Count);
        }

        return routes;
    }
}
