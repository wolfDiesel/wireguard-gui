using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class PolicyRoutingSetup(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    ILogger<PolicyRoutingSetup> logger) : IPolicyRoutingSetup
{
    private const int RulePreference = 100;

    private readonly ConcurrentDictionary<string, string> _syncedRoutes = new(StringComparer.Ordinal);

    public bool IsAvailable => processRunner.IsCommandAvailable("ip");

    public async Task PrepareConnectionAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || profile.Backend != BackendKind.Nmcli)
            return;

        await RunNmcliNeverDefaultAsync(profile, cancellationToken).ConfigureAwait(false);
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

            await EnsureNmcliNeverDefaultAsync(profile, reconnect: true, cancellationToken).ConfigureAwait(false);
            await SyncDestinationRulesAsync(profile, iface, routes, cancellationToken).ConfigureAwait(false);
            await EnsureEndpointRouteAsync(profile, cancellationToken).ConfigureAwait(false);
            _syncedRoutes[profile.Id] = PolicyRoutingNaming.NormalizeRoutesKey(routes);
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
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new PolicyRoutingSyncResult(false, "Policy routing requires ip");

        var key = PolicyRoutingNaming.NormalizeRoutesKey(routes);
        if (_syncedRoutes.TryGetValue(profile.Id, out var previous) &&
            string.Equals(previous, key, StringComparison.Ordinal))
        {
            logger.LogInformation("Policy routing {Profile}: routes unchanged ({Count})", profile.Name, routes.Count);
            return new PolicyRoutingSyncResult(false, null);
        }

        try
        {
            var iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(iface))
                return new PolicyRoutingSyncResult(false, "WireGuard interface not found");

            await SyncDestinationRulesAsync(profile, iface, routes, cancellationToken).ConfigureAwait(false);
            _syncedRoutes[profile.Id] = key;
            logger.LogInformation(
                "Policy routing synced for {Profile} ({Count} routes)",
                profile.Name,
                routes.Count);
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

        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var mark = FormatFwMark(PolicyRoutingNaming.FwMark(profile.Id));
        var chain = PolicyRoutingNaming.ChainName(profile.Id);
        var nftTable = PolicyRoutingNaming.NftTable;

        await ClearRulesForTableAsync(table, cancellationToken).ConfigureAwait(false);
        await RunIpPrivilegedAsync(["route", "flush", "table", table.ToString()], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedAsync(["-6", "route", "flush", "table", table.ToString()], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedAsync(["rule", "flush", "fwmark", mark], cancellationToken).ConfigureAwait(false);
        await RunIpPrivilegedAsync(["-6", "rule", "flush", "fwmark", mark], cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "chain", "inet", nftTable, chain],
            cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "set", "inet", nftTable, PolicyRoutingNaming.HostsSetName(profile.Id)],
            cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "set", "inet", nftTable, PolicyRoutingNaming.NetsSetName(profile.Id)],
            cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "set", "inet", nftTable, PolicyRoutingNaming.Hosts6SetName(profile.Id)],
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Policy routing torn down for {Profile}", profile.Name);
    }

    private async Task SyncDestinationRulesAsync(
        VpnProfile profile,
        string iface,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken)
    {
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var tableText = table.ToString();
        var ipv4Routes = routes.Where(r => !r.Contains(':', StringComparison.Ordinal)).ToList();
        var ipv6Routes = routes.Where(r => r.Contains(':', StringComparison.Ordinal)).ToList();
        var ipv6Capable = ipv6Routes.Count > 0 &&
            await IsInterfaceIpv6CapableAsync(iface, cancellationToken).ConfigureAwait(false);

        if (ipv6Routes.Count > 0 && !ipv6Capable)
        {
            logger.LogInformation(
                "Policy routing {Profile}: skipping {Count} IPv6 routes — {Interface} has no IPv6",
                profile.Name,
                ipv6Routes.Count,
                iface);
        }

        await ClearRulesForTableAsync(table, cancellationToken).ConfigureAwait(false);

        foreach (var route in ipv4Routes)
        {
            var trimmed = route.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            await RunIpPrivilegedAsync(
                ["rule", "add", "pref", RulePreference.ToString(), "to", trimmed, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
        }

        await RunIpPrivilegedAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);

        if (ipv6Capable)
        {
            foreach (var route in ipv6Routes)
            {
                var trimmed = route.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                await RunIpPrivilegedAsync(
                    ["-6", "rule", "add", "pref", RulePreference.ToString(), "to", trimmed, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }

            await RunIpPrivilegedAsync(
                ["-6", "route", "replace", "default", "dev", iface, "table", tableText],
                cancellationToken).ConfigureAwait(false);
        }

        if (ipv4Routes.Count == 0 && !ipv6Capable)
            throw new InvalidOperationException("No applicable IPv4 routes to install");
    }

    private async Task ClearRulesForTableAsync(int table, CancellationToken cancellationToken)
    {
        var tableText = table.ToString();
        await RunIpPrivilegedIgnoringErrorsAsync(["rule", "flush", "table", tableText], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(["-6", "rule", "flush", "table", tableText], cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> IsInterfaceIpv6CapableAsync(string iface, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "ip",
            ["-6", "addr", "show", "dev", iface],
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            return false;

        return result.StandardOutput.Contains("inet6 ", StringComparison.Ordinal);
    }

    private async Task EnsureNmcliNeverDefaultAsync(
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

    private Task RunNmcliNeverDefaultAsync(VpnProfile profile, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync(
            "nmcli",
            ["connection", "modify", profile.ConnectionName, "ipv4.never-default", "yes", "ipv6.never-default", "yes"],
            cancellationToken);

    private async Task EnsureEndpointRouteAsync(VpnProfile profile, CancellationToken cancellationToken)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var configContent = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var endpointHost = configParser.ReadEndpointHost(configContent);
        if (string.IsNullOrWhiteSpace(endpointHost))
            return;

        var endpointIp = await ResolveHostAsync(endpointHost, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(endpointIp))
            return;

        var gatewayResult = await processRunner.RunAsync(
            "ip",
            ["route", "show", "default"],
            cancellationToken).ConfigureAwait(false);
        if (!gatewayResult.IsSuccess)
            return;

        var gatewayLine = gatewayResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !line.Contains(" dev home", StringComparison.OrdinalIgnoreCase) &&
                                    !line.Contains(" dev wg", StringComparison.OrdinalIgnoreCase));
        if (gatewayLine is null)
            gatewayLine = gatewayResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (gatewayLine is null)
            return;

        var parts = gatewayLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var devIndex = Array.IndexOf(parts, "dev");
        if (devIndex < 0 || devIndex + 1 >= parts.Length)
            return;

        var dev = parts[devIndex + 1];
        var viaIndex = Array.IndexOf(parts, "via");
        var via = viaIndex >= 0 && viaIndex + 1 < parts.Length ? parts[viaIndex + 1] : null;

        if (via is null)
        {
            await RunIpPrivilegedAsync(["route", "replace", $"{endpointIp}/32", "dev", dev], cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await RunIpPrivilegedAsync(
                ["route", "replace", $"{endpointIp}/32", "via", via, "dev", dev],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> ResolveWireGuardInterfaceAsync(
        VpnProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Backend == BackendKind.Nmcli && processRunner.IsCommandAvailable("nmcli"))
        {
            var nmResult = await processRunner.RunAsync(
                "nmcli",
                ["-g", "wireguard.interface", "connection", "show", profile.ConnectionName],
                cancellationToken).ConfigureAwait(false);
            var nmIface = nmResult.StandardOutput.Trim();
            if (!string.IsNullOrWhiteSpace(nmIface))
                return nmIface;
        }

        var result = await processRunner.RunAsync("wg", ["show", "interfaces"], cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return profile.ConnectionName;

        var interfaces = result.StandardOutput
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return interfaces.FirstOrDefault(i => string.Equals(i, profile.ConnectionName, StringComparison.Ordinal))
            ?? interfaces.FirstOrDefault()
            ?? profile.ConnectionName;
    }

    private async Task<string?> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        if (global::System.Net.IPAddress.TryParse(host, out _))
            return host;

        if (!processRunner.IsCommandAvailable("dig"))
            return null;

        var result = await processRunner.RunAsync(
            "dig",
            ["+short", "A", host],
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            return null;

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private async Task RunIpPrivilegedAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.StandardError.Trim());
    }

    private Task RunIpPrivilegedIgnoringErrorsAsync(string[] arguments, CancellationToken cancellationToken) =>
        RunPrivilegedIgnoringErrorsAsync("ip", arguments, cancellationToken);

    private Task RunPrivilegedIgnoringErrorsAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync(fileName, arguments, cancellationToken);

    private static string FormatFwMark(uint mark) => $"0x{mark:x}";
}
