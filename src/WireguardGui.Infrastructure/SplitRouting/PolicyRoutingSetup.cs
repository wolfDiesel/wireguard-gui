using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class PolicyRoutingSetup(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    IDomainRouteDnsProxy dnsProxy,
    IpRuleManager ipRuleManager,
    NftSetManager nftSetManager,
    TunnelDnsManager tunnelDnsManager,
    EndpointRouteGuard endpointRouteGuard,
    ILogger<PolicyRoutingSetup> logger) : IPolicyRoutingSetup
{
    private readonly ConcurrentDictionary<string, string> _syncedRoutes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _interfaces = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _bulkRoutes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _installedHosts =
        new(StringComparer.Ordinal);

    public bool IsAvailable => processRunner.IsCommandAvailable("ip");

    public async Task PrepareConnectionAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || profile.Backend != BackendKind.Nmcli)
            return;

        await tunnelDnsManager.RunNmcliNeverDefaultAsync(profile, cancellationToken).ConfigureAwait(false);
        await tunnelDnsManager.ConfigureNmcliDnsFromConfigAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PolicyRoutingApplyResult> ApplyAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new PolicyRoutingApplyResult(false, "Policy routing requires ip");

        try
        {
            var iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(iface))
                return new PolicyRoutingApplyResult(false, "WireGuard interface not found");

            await tunnelDnsManager.EnsureNmcliNeverDefaultAsync(profile, reconnect: true, cancellationToken)
                .ConfigureAwait(false);
            await ipRuleManager.CleanupOrphanPolicyTablesAsync(profile, cancellationToken).ConfigureAwait(false);
            await nftSetManager.CleanupBrokenNftMarkPathAsync(profile, cancellationToken).ConfigureAwait(false);
            await ipRuleManager.SyncDestinationRulesAsync(
                    profile,
                    iface,
                    routes,
                    replaceAll: true,
                    readdAll: false,
                    _bulkRoutes,
                    cancellationToken)
                .ConfigureAwait(false);
            await endpointRouteGuard.EnsureEndpointRouteAsync(profile, cancellationToken).ConfigureAwait(false);
            await tunnelDnsManager.EnsureTunnelDnsAsync(profile, iface, _installedHosts, cancellationToken)
                .ConfigureAwait(false);
            _syncedRoutes[profile.Id] = PolicyRoutingNaming.NormalizeRoutesKey(routes);
            _interfaces[profile.Id] = iface;
            ReplaceBulkAndHostState(profile.Id, routes);
            logger.LogInformation(
                "Policy routing applied for {Profile} on {Interface} ({Count} routes)",
                profile.Name,
                iface,
                routes.Count);
            return new PolicyRoutingApplyResult(true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy routing apply failed for {Profile}", profile.Name);
            return new PolicyRoutingApplyResult(false, ex.Message);
        }
    }

    public async Task<PolicyRoutingSyncResult> SyncRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        if (!IsAvailable)
            return new PolicyRoutingSyncResult(false, "Policy routing requires ip");

        try
        {
            var iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(iface))
                return new PolicyRoutingSyncResult(false, "WireGuard interface not found");

            var key = PolicyRoutingNaming.NormalizeRoutesKey(routes);
            var unchanged = !force &&
                _syncedRoutes.TryGetValue(profile.Id, out var previous) &&
                string.Equals(previous, key, StringComparison.Ordinal);

            if (unchanged)
            {
                await ipRuleManager.EnsurePolicyTableDefaultRouteAsync(profile, iface, cancellationToken)
                    .ConfigureAwait(false);
                await tunnelDnsManager.EnsureTunnelDnsAsync(profile, iface, _installedHosts, cancellationToken)
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "Policy routing {Profile}: routes unchanged ({Count}), refreshed table baseline and DNS",
                    profile.Name,
                    routes.Count);
                return new PolicyRoutingSyncResult(false, null);
            }

            await nftSetManager.CleanupBrokenNftMarkPathAsync(profile, cancellationToken).ConfigureAwait(false);
            await ipRuleManager.SyncDestinationRulesAsync(
                    profile,
                    iface,
                    routes,
                    replaceAll: false,
                    readdAll: force,
                    _bulkRoutes,
                    cancellationToken)
                .ConfigureAwait(false);
            await tunnelDnsManager.EnsureTunnelDnsAsync(profile, iface, _installedHosts, cancellationToken)
                .ConfigureAwait(false);
            _syncedRoutes[profile.Id] = key;
            _interfaces[profile.Id] = iface;
            MergeBulkAndHostState(profile.Id, routes);
            logger.LogInformation(
                "Policy routing synced for {Profile} ({Count} routes, force={Force})",
                profile.Name,
                routes.Count,
                force);
            return new PolicyRoutingSyncResult(true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Policy routing sync failed for {Profile}", profile.Name);
            return new PolicyRoutingSyncResult(false, ex.Message);
        }
    }

    public async Task TeardownAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return;

        _syncedRoutes.TryRemove(profile.Id, out _);
        _interfaces.TryRemove(profile.Id, out _);
        _bulkRoutes.TryRemove(profile.Id, out _);
        _installedHosts.TryRemove(profile.Id, out _);
        await dnsProxy.StopAsync(cancellationToken).ConfigureAwait(false);

        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);

        await ipRuleManager.ClearRulesForTableAsync(table, cancellationToken).ConfigureAwait(false);
        var iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(iface))
            await tunnelDnsManager.ClearTunnelDnsAsync(iface, cancellationToken).ConfigureAwait(false);

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "flush", "table", table.ToString()],
            cancellationToken).ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(
            ["-6", "route", "flush", "table", table.ToString()],
            cancellationToken).ConfigureAwait(false);
        await nftSetManager.CleanupBrokenNftMarkPathAsync(profile, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Policy routing torn down for {Profile}", profile.Name);
    }

    public async Task AddHostRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> hostCidrs,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || hostCidrs.Count == 0)
            return;

        await ipRuleManager.AddHostRoutesAsync(
            profile,
            hostCidrs,
            _installedHosts,
            ResolveInterfaceForHostsAsync,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveInterfaceForHostsAsync(string profileId, CancellationToken cancellationToken)
    {
        if (_interfaces.TryGetValue(profileId, out var cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
            return null;

        var iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(iface))
            _interfaces[profileId] = iface;
        return iface;
    }

    private void ReplaceBulkAndHostState(string profileId, IReadOnlyList<string> routes)
    {
        _bulkRoutes[profileId] = PolicyRoutingCommands.NormalizeRouteSet(routes);
        var installed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            var host = PolicyRoutingCommands.NormalizeHostCidr(route);
            if (host is not null)
                installed[host] = 0;
        }

        _installedHosts[profileId] = installed;
    }

    private void MergeBulkAndHostState(string profileId, IReadOnlyList<string> routes)
    {
        _bulkRoutes[profileId] = PolicyRoutingCommands.NormalizeRouteSet(routes);
        var installed = _installedHosts.GetOrAdd(
            profileId,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        foreach (var route in routes)
        {
            var host = PolicyRoutingCommands.NormalizeHostCidr(route);
            if (host is not null)
                installed[host] = 0;
        }
    }

    private async Task<string?> ResolveWireGuardInterfaceAsync(
        VpnProfile profile,
        CancellationToken cancellationToken) =>
        await endpointRouteGuard.ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);

    private Task RunIpPrivilegedIgnoringErrorsAsync(string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken);
}