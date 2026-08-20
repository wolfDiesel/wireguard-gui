using System.Net;
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

        foreach (var entry in domains)
        {
            if (TryParseStaticRoute(entry, out var staticRoute))
            {
                routes.Add(staticRoute);
                logger.LogInformation("Custom route {Route} added directly", staticRoute);
                continue;
            }

            foreach (var domain in ExpandDomainVariants(entry))
            {
                progress?.Report(new SplitRoutingProgress("Progress_Resolve_Domain", domain));
                logger.LogInformation("Resolving domain {Domain}…", domain);

                var ips = await dnsResolver.ResolveIpv4Async(domain, cancellationToken);
                foreach (var ip in ips)
                    routes.Add($"{ip}/32");

                var ipv6 = await dnsResolver.ResolveIpv6Async(domain, cancellationToken);
                foreach (var ip in ipv6)
                    routes.Add($"{ip}/128");

                logger.LogInformation("Domain {Domain}: {Count} addresses", domain, ips.Count + ipv6.Count);
            }
        }

        return routes;
    }

    internal static IEnumerable<string> ExpandDomainVariants(string domain)
    {
        yield return domain;
        if (!domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            yield return "www." + domain;
    }

    internal static bool TryParseStaticRoute(string entry, out string route)
    {
        route = string.Empty;
        var slash = entry.IndexOf('/');
        if (slash <= 0 || slash >= entry.Length - 1)
            return false;

        var network = entry[..slash].Trim();
        var prefixText = entry[(slash + 1)..].Trim();
        if (!IPAddress.TryParse(network, out _))
            return false;
        if (!int.TryParse(prefixText, out var prefix))
            return false;

        var maxPrefix = network.Contains(':', StringComparison.Ordinal) ? 128 : 32;
        if (prefix < 0 || prefix > maxPrefix)
            return false;

        route = $"{network}/{prefix}";
        return true;
    }
}
