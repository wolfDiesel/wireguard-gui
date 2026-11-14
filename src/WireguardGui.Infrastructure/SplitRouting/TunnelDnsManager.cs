using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class TunnelDnsManager(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    ILogger<TunnelDnsManager> logger,
    TunnelHealthProbe tunnelHealthProbe)
{
    private readonly ConcurrentDictionary<string, bool> _degraded = new(StringComparer.Ordinal);

    /// <summary>
    /// True, когда split DNS для профиля снят, потому что туннель не обслуживает
    /// запросы (нет handshake). Фоновый refresh по этому признаку повторяет попытку.
    /// </summary>
    public bool IsSplitDnsDegraded(string profileId) =>
        _degraded.TryGetValue(profileId, out var degraded) && degraded;

    public async Task EnsureTunnelDnsAsync(
        VpnProfile profile,
        string iface,
        ConcurrentDictionary<string, ConcurrentDictionary<string, long>> installedHosts,
        CancellationToken cancellationToken)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var configContent = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var dnsServers = configParser.ResolveTunnelDnsServers(configContent);
        if (dnsServers.Count == 0)
        {
            logger.LogWarning("Policy routing {Profile}: no tunnel DNS configured", profile.Name);
            return;
        }

        var health = await tunnelHealthProbe.CheckAsync(profile, iface, cancellationToken).ConfigureAwait(false);
        if (!health.AllowsSplitDns)
        {
            var wasDegraded = IsSplitDnsDegraded(profile.Id);
            _degraded[profile.Id] = true;
            logger.LogWarning(
                "Policy routing {Profile}: split DNS withheld on {Interface} ({Health}) — split domains keep resolving via the local resolver",
                profile.Name,
                iface,
                health.Describe());
            await RemoveDnsPolicyRulesAsync(profile, dnsServers, installedHosts, cancellationToken)
                .ConfigureAwait(false);
            await ClearTunnelDnsAsync(iface, cancellationToken).ConfigureAwait(false);
            if (!wasDegraded)
                await FlushResolverCacheAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsSplitDnsDegraded(profile.Id))
            logger.LogInformation(
                "Policy routing {Profile}: tunnel is back ({Health}), restoring split DNS on {Interface}",
                profile.Name,
                health.Describe(),
                iface);

        _degraded[profile.Id] = false;

        if (profile.Backend == BackendKind.Nmcli)
            await ConfigureNmcliDnsFromConfigAsync(profile, cancellationToken).ConfigureAwait(false);

        var routingDomains = DnsRouteSuffixes.ForTunnelDns(profile.SplitRouting);
        await ConfigureResolvedDnsAsync(iface, dnsServers, routingDomains, cancellationToken).ConfigureAwait(false);
        await EnsureDnsPolicyRoutesAsync(profile, iface, dnsServers, installedHosts, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Policy routing {Profile}: tunnel DNS {Servers} on {Interface} for {DomainCount} split domains",
            profile.Name,
            string.Join(", ", dnsServers),
            iface,
            routingDomains.Count);
    }

    public async Task ClearTunnelDnsAsync(string iface, CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("resolvectl"))
            return;

        await RunPrivilegedIgnoringErrorsAsync("resolvectl", ["revert", iface], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EnsureNmcliNeverDefaultAsync(
        VpnProfile profile,
        bool reconnect,
        CancellationToken cancellationToken)
    {
        if (profile.Backend != BackendKind.Nmcli)
            return;

        await RunNmcliNeverDefaultAsync(profile, cancellationToken).ConfigureAwait(false);
        if (reconnect)
        {
            await processRunner.RunPrivilegedAsync(
                "nmcli",
                ["connection", "up", profile.ConnectionName],
                cancellationToken).ConfigureAwait(false);
        }
    }

    public Task RunNmcliNeverDefaultAsync(VpnProfile profile, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync(
            "nmcli",
            ["connection", "modify", profile.ConnectionName, "ipv4.never-default", "yes", "ipv6.never-default", "yes"],
            cancellationToken);

    public async Task ConfigureNmcliDnsFromConfigAsync(VpnProfile profile, CancellationToken cancellationToken)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var configContent = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var dnsServers = configParser.ResolveTunnelDnsServers(configContent);
        if (dnsServers.Count == 0)
            return;

        await ConfigureNmcliDnsAsync(profile, dnsServers, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConfigureNmcliDnsAsync(
        VpnProfile profile,
        IReadOnlyList<string> dnsServers,
        CancellationToken cancellationToken)
    {
        var dnsCsv = string.Join(",", dnsServers);
        await processRunner.RunPrivilegedAsync(
            "nmcli",
            [
                "connection", "modify", profile.ConnectionName,
                "ipv4.ignore-auto-dns", "yes",
                "ipv6.ignore-auto-dns", "yes",
                "ipv4.dns", dnsCsv,
            ],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ConfigureResolvedDnsAsync(
        string iface,
        IReadOnlyList<string> dnsServers,
        IReadOnlyList<string> routingDomains,
        CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("resolvectl"))
        {
            logger.LogWarning(
                "Tunnel DNS skipped on {Interface}: resolvectl not found (install systemd / systemd-resolved)",
                iface);
            return;
        }

        var dnsArgs = new List<string> { "dns", iface };
        dnsArgs.AddRange(dnsServers);
        await RunPrivilegedIgnoringErrorsAsync("resolvectl", dnsArgs, cancellationToken).ConfigureAwait(false);

        var domainArgs = new List<string> { "domain", iface };
        domainArgs.AddRange(routingDomains.Select(static domain => "~" + domain));
        await RunPrivilegedIgnoringErrorsAsync("resolvectl", domainArgs, cancellationToken).ConfigureAwait(false);

        await RunPrivilegedIgnoringErrorsAsync(
            "resolvectl",
            ["default-route", iface, "no"],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDnsPolicyRoutesAsync(
        VpnProfile profile,
        string iface,
        IReadOnlyList<string> dnsServers,
        ConcurrentDictionary<string, ConcurrentDictionary<string, long>> installedHosts,
        CancellationToken cancellationToken)
    {
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var tableText = table.ToString();

        foreach (var server in dnsServers)
        {
            if (server.Contains(':', StringComparison.Ordinal))
                continue;

            var cidr = $"{server}/32";
            await RunIpPrivilegedIgnoringErrorsAsync(
                ["rule", "add", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", cidr, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
            installedHosts
                .GetOrAdd(profile.Id, _ => new ConcurrentDictionary<string, long>(StringComparer.Ordinal))
                [cidr] = Environment.TickCount64;
        }

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveDnsPolicyRulesAsync(
        VpnProfile profile,
        IReadOnlyList<string> dnsServers,
        ConcurrentDictionary<string, ConcurrentDictionary<string, long>> installedHosts,
        CancellationToken cancellationToken)
    {
        if (!installedHosts.TryGetValue(profile.Id, out var installed))
            return;

        var tableText = PolicyRoutingNaming.RoutingTableId(profile.Id).ToString();
        foreach (var server in dnsServers)
        {
            if (server.Contains(':', StringComparison.Ordinal))
                continue;

            var cidr = $"{server}/32";
            if (!installed.TryRemove(cidr, out _))
                continue;

            await RunIpPrivilegedIgnoringErrorsAsync(
                ["rule", "del", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", cidr, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FlushResolverCacheAsync(CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("resolvectl"))
            return;

        await RunPrivilegedIgnoringErrorsAsync("resolvectl", ["flush-caches"], cancellationToken)
            .ConfigureAwait(false);
    }

    private Task RunIpPrivilegedIgnoringErrorsAsync(string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken);

    private Task RunPrivilegedIgnoringErrorsAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync(fileName, arguments, cancellationToken);
}
