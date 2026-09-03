using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class PolicyRoutingSetup(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    IDomainRouteDnsProxy dnsProxy,
    ILogger<PolicyRoutingSetup> logger) : IPolicyRoutingSetup
{
    private const int RulePreference = 100;

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

        await RunNmcliNeverDefaultAsync(profile, cancellationToken).ConfigureAwait(false);
        await ConfigureNmcliDnsFromConfigAsync(profile, cancellationToken).ConfigureAwait(false);
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
            await CleanupOrphanPolicyTablesAsync(profile, cancellationToken).ConfigureAwait(false);
            await CleanupBrokenNftMarkPathAsync(profile, cancellationToken).ConfigureAwait(false);
            await SyncDestinationRulesAsync(
                    profile,
                    iface,
                    routes,
                    replaceAll: true,
                    readdAll: false,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureEndpointRouteAsync(profile, cancellationToken).ConfigureAwait(false);
            await EnsureTunnelDnsAsync(profile, iface, cancellationToken).ConfigureAwait(false);
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
                await EnsurePolicyTableDefaultRouteAsync(profile, iface, cancellationToken).ConfigureAwait(false);
                await EnsureTunnelDnsAsync(profile, iface, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Policy routing {Profile}: routes unchanged ({Count}), refreshed table baseline and DNS",
                    profile.Name,
                    routes.Count);
                return new PolicyRoutingSyncResult(false, null);
            }

            await CleanupBrokenNftMarkPathAsync(profile, cancellationToken).ConfigureAwait(false);
            await SyncDestinationRulesAsync(
                    profile,
                    iface,
                    routes,
                    replaceAll: false,
                    readdAll: force,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureTunnelDnsAsync(profile, iface, cancellationToken).ConfigureAwait(false);
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

        await ClearRulesForTableAsync(table, cancellationToken).ConfigureAwait(false);
        var iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(iface))
            await ClearTunnelDnsAsync(iface, cancellationToken).ConfigureAwait(false);

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "flush", "table", table.ToString()],
            cancellationToken).ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(
            ["-6", "route", "flush", "table", table.ToString()],
            cancellationToken).ConfigureAwait(false);
        await CleanupBrokenNftMarkPathAsync(profile, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Policy routing torn down for {Profile}", profile.Name);
    }

    public async Task AddHostRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> hostCidrs,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || hostCidrs.Count == 0)
            return;

        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var tableText = table.ToString();
        var installed = _installedHosts.GetOrAdd(
            profile.Id,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

        var pending = new List<string>();
        foreach (var cidr in hostCidrs)
        {
            var trimmed = NormalizeHostCidr(cidr);
            if (trimmed is null)
                continue;
            if (!installed.TryAdd(trimmed, 0))
                continue;
            pending.Add(trimmed);
        }

        if (pending.Count == 0)
            return;

        if (!_interfaces.TryGetValue(profile.Id, out var iface) || string.IsNullOrWhiteSpace(iface))
        {
            iface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(iface))
            {
                foreach (var item in pending)
                    installed.TryRemove(item, out _);
                return;
            }

            _interfaces[profile.Id] = iface;
        }

        var script = BuildAddHostRulesScript(pending, tableText);
        var result = await processRunner.RunPrivilegedShellAsync(script, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            foreach (var item in pending)
                installed.TryRemove(item, out _);
            logger.LogWarning(
                "Policy routing {Profile}: failed to add {Count} monitored hosts: {Error}",
                profile.Name,
                pending.Count,
                result.StandardError.Trim());
            return;
        }

        logger.LogInformation(
            "Policy routing {Profile}: added {Count} monitored host routes via shared privileged session",
            profile.Name,
            pending.Count);
    }

    private void ReplaceBulkAndHostState(string profileId, IReadOnlyList<string> routes)
    {
        _bulkRoutes[profileId] = NormalizeRouteSet(routes);
        var installed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            var host = NormalizeHostCidr(route);
            if (host is not null)
                installed[host] = 0;
        }

        _installedHosts[profileId] = installed;
    }

    private void MergeBulkAndHostState(string profileId, IReadOnlyList<string> routes)
    {
        _bulkRoutes[profileId] = NormalizeRouteSet(routes);
        var installed = _installedHosts.GetOrAdd(
            profileId,
            _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        foreach (var route in routes)
        {
            var host = NormalizeHostCidr(route);
            if (host is not null)
                installed[host] = 0;
        }
    }

    private static HashSet<string> NormalizeRouteSet(IEnumerable<string> routes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            var trimmed = route.Trim();
            if (trimmed.Length > 0)
                set.Add(trimmed);
        }

        return set;
    }

    private static string? NormalizeHostCidr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (trimmed.EndsWith("/128", StringComparison.Ordinal))
                return IPAddress.TryParse(trimmed[..^4], out _) ? trimmed : null;
            if (IPAddress.TryParse(trimmed, out var ip6) && ip6.AddressFamily == AddressFamily.InterNetworkV6)
                return ip6 + "/128";
            return null;
        }

        if (trimmed.EndsWith("/32", StringComparison.Ordinal))
            return IPAddress.TryParse(trimmed[..^3], out _) ? trimmed : null;
        if (IPAddress.TryParse(trimmed, out var ip4) && ip4.AddressFamily == AddressFamily.InterNetwork)
            return ip4 + "/32";
        return null;
    }

    private static string BuildAddHostRulesScript(IReadOnlyList<string> cidrs, string tableText)
    {
        var builder = new StringBuilder();
        foreach (var cidr in cidrs)
        {
            if (cidr.Contains(':', StringComparison.Ordinal))
            {
                builder.Append("ip -6 rule add pref ")
                    .Append(RulePreference)
                    .Append(" to ")
                    .Append(ShellEscape(cidr))
                    .Append(" lookup ")
                    .Append(tableText)
                    .Append(" 2>/dev/null || true\n");
            }
            else
            {
                builder.Append("ip rule add pref ")
                    .Append(RulePreference)
                    .Append(" to ")
                    .Append(ShellEscape(cidr))
                    .Append(" lookup ")
                    .Append(tableText)
                    .Append(" 2>/dev/null || true\n");
            }
        }

        return builder.ToString();
    }

    private static string ShellEscape(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private async Task SyncDestinationRulesAsync(
        VpnProfile profile,
        string iface,
        IReadOnlyList<string> routes,
        bool replaceAll,
        bool readdAll,
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

        if (replaceAll)
        {
            await ClearRulesForTableAsync(table, cancellationToken).ConfigureAwait(false);
            await InstallDestinationRulesAsync(
                    ipv4Routes,
                    ipv6Capable ? ipv6Routes : [],
                    tableText,
                    iface,
                    ipv6Capable,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await MergeDestinationRulesAsync(
                    profile.Id,
                    ipv4Routes,
                    ipv6Capable ? ipv6Routes : [],
                    tableText,
                    iface,
                    ipv6Capable,
                    readdAll,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (ipv4Routes.Count == 0 && !ipv6Capable)
            throw new InvalidOperationException("No applicable IPv4 routes to install");
    }

    private async Task MergeDestinationRulesAsync(
        string profileId,
        IReadOnlyList<string> ipv4Routes,
        IReadOnlyList<string> ipv6Routes,
        string tableText,
        string iface,
        bool ipv6Capable,
        bool readdAll,
        CancellationToken cancellationToken)
    {
        var desired = NormalizeRouteSet(ipv4Routes.Concat(ipv6Capable ? ipv6Routes : []));
        _bulkRoutes.TryGetValue(profileId, out var previous);
        previous ??= [];

        foreach (var route in desired)
        {
            if (!readdAll && previous.Contains(route))
                continue;

            if (route.Contains(':', StringComparison.Ordinal))
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["-6", "rule", "add", "pref", RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["rule", "add", "pref", RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var route in previous)
        {
            if (desired.Contains(route))
                continue;

            if (NormalizeHostCidr(route) is not null)
                continue;

            if (route.Contains(':', StringComparison.Ordinal))
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["-6", "rule", "del", "pref", RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["rule", "del", "pref", RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
        if (ipv6Capable)
        {
            await RunIpPrivilegedIgnoringErrorsAsync(
                ["-6", "route", "replace", "default", "dev", iface, "table", tableText],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InstallDestinationRulesAsync(
        IReadOnlyList<string> ipv4Routes,
        IReadOnlyList<string> ipv6Routes,
        string tableText,
        string iface,
        bool ipv6Capable,
        CancellationToken cancellationToken)
    {
        foreach (var route in ipv4Routes)
        {
            var trimmed = route.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            await RunIpPrivilegedIgnoringErrorsAsync(
                ["rule", "add", "pref", RulePreference.ToString(), "to", trimmed, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
        }

        await RunIpPrivilegedAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);

        if (!ipv6Capable)
            return;

        foreach (var route in ipv6Routes)
        {
            var trimmed = route.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            await RunIpPrivilegedIgnoringErrorsAsync(
                ["-6", "rule", "add", "pref", RulePreference.ToString(), "to", trimmed, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
        }

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["-6", "route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupBrokenNftMarkPathAsync(VpnProfile profile, CancellationToken cancellationToken)
    {
        var mark = FormatFwMark(PolicyRoutingNaming.FwMark(profile.Id));
        var chain = PolicyRoutingNaming.ChainName(profile.Id);
        var nftTable = PolicyRoutingNaming.NftTable;

        await RunIpPrivilegedIgnoringErrorsAsync(["rule", "flush", "fwmark", mark], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(["-6", "rule", "flush", "fwmark", mark], cancellationToken)
            .ConfigureAwait(false);

        if (!processRunner.IsCommandAvailable("nft"))
            return;

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
    }

    private async Task ClearRulesForTableAsync(int table, CancellationToken cancellationToken)
    {
        var tableText = table.ToString();
        await RunIpPrivilegedIgnoringErrorsAsync(["rule", "flush", "table", tableText], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(["-6", "rule", "flush", "table", tableText], cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsurePolicyTableDefaultRouteAsync(
        VpnProfile profile,
        string iface,
        CancellationToken cancellationToken)
    {
        var tableText = PolicyRoutingNaming.RoutingTableId(profile.Id).ToString();
        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(
            ["-6", "route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupOrphanPolicyTablesAsync(VpnProfile profile, CancellationToken cancellationToken)
    {
        var keepTable = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var listed = await processRunner.RunAsync("ip", ["rule", "list"], cancellationToken)
            .ConfigureAwait(false);
        if (!listed.IsSuccess)
            return;

        var orphans = new HashSet<int>();
        foreach (var line in listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith($"{RulePreference}:", StringComparison.Ordinal))
                continue;

            var lookupIndex = trimmed.LastIndexOf("lookup ", StringComparison.Ordinal);
            if (lookupIndex < 0)
                continue;

            var tableText = trimmed[(lookupIndex + "lookup ".Length)..].Trim();
            var space = tableText.IndexOf(' ');
            if (space > 0)
                tableText = tableText[..space];

            if (!int.TryParse(tableText, out var table))
                continue;
            if (!PolicyRoutingNaming.IsManagedRoutingTable(table) || table == keepTable)
                continue;

            orphans.Add(table);
        }

        foreach (var table in orphans)
        {
            logger.LogInformation(
                "Policy routing {Profile}: clearing orphan table {Table} (keep {Keep})",
                profile.Name,
                table,
                keepTable);
            await ClearRulesForTableAsync(table, cancellationToken).ConfigureAwait(false);
            await RunIpPrivilegedIgnoringErrorsAsync(
                ["route", "flush", "table", table.ToString()],
                cancellationToken).ConfigureAwait(false);
            await RunIpPrivilegedIgnoringErrorsAsync(
                ["-6", "route", "flush", "table", table.ToString()],
                cancellationToken).ConfigureAwait(false);
        }
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

    private async Task EnsureTunnelDnsAsync(
        VpnProfile profile,
        string iface,
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

        if (profile.Backend == BackendKind.Nmcli)
            await ConfigureNmcliDnsFromConfigAsync(profile, cancellationToken).ConfigureAwait(false);

        await ConfigureResolvedDnsAsync(iface, dnsServers, cancellationToken).ConfigureAwait(false);
        await EnsureDnsPolicyRoutesAsync(profile, iface, dnsServers, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Policy routing {Profile}: tunnel DNS {Servers} on {Interface}",
            profile.Name,
            string.Join(", ", dnsServers),
            iface);
    }

    private async Task ConfigureNmcliDnsFromConfigAsync(VpnProfile profile, CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("resolvectl"))
            return;

        var dnsArgs = new List<string> { "dns", iface };
        dnsArgs.AddRange(dnsServers);
        await RunPrivilegedIgnoringErrorsAsync("resolvectl", dnsArgs, cancellationToken).ConfigureAwait(false);

        if (TunnelDnsServers.ShouldCaptureAllDns(dnsServers))
        {
            await RunPrivilegedIgnoringErrorsAsync(
                "resolvectl",
                ["domain", iface, "~."],
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await RunPrivilegedIgnoringErrorsAsync(
            "resolvectl",
            ["domain", iface],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearTunnelDnsAsync(string iface, CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("resolvectl"))
            return;

        await RunPrivilegedIgnoringErrorsAsync("resolvectl", ["revert", iface], cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureDnsPolicyRoutesAsync(
        VpnProfile profile,
        string iface,
        IReadOnlyList<string> dnsServers,
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
                ["rule", "add", "pref", RulePreference.ToString(), "to", cidr, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
            _installedHosts
                .GetOrAdd(profile.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
                [cidr] = 0;
        }

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
    }

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
