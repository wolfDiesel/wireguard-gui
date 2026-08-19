using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class TelegramSplitRouteSource(
    DomainDnsResolver dnsResolver,
    IPolicyRoutingSetup policyRoutingSetup) : ISplitRouteSource
{
    public int Priority => 0;

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Telegram;

    public async Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SplitRoutingProgress("Progress_Routes_Telegram"));

        var routes = SplitRoutingConstants.TelegramRoutes.ToList();
        if (!policyRoutingSetup.IsAvailable)
            return routes;

        dnsResolver.EnsureDigAvailable();
        foreach (var domain in SplitRoutingConstants.TelegramResolveDomains)
        {
            var ipv6 = await dnsResolver.ResolveIpv6Async(domain, cancellationToken).ConfigureAwait(false);
            routes.AddRange(ipv6.Select(ip => $"{ip}/128"));
        }

        return routes;
    }
}
