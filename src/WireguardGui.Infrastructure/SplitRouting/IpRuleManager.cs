using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class IpRuleManager(
    IProcessRunner processRunner,
    ILogger<IpRuleManager> logger)
{
    public async Task SyncDestinationRulesAsync(
        VpnProfile profile,
        string iface,
        IReadOnlyList<string> routes,
        bool replaceAll,
        bool readdAll,
        IReadOnlyDictionary<string, HashSet<string>> bulkRoutes,
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
                    bulkRoutes,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (ipv4Routes.Count == 0 && !ipv6Capable)
            throw new InvalidOperationException("No applicable IPv4 routes to install");
    }

    public async Task AddHostRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> hostCidrs,
        ConcurrentDictionary<string, ConcurrentDictionary<string, long>> installedHosts,
        IReadOnlyDictionary<string, HashSet<string>> bulkRoutes,
        Func<string, CancellationToken, Task<string?>> resolveInterface,
        CancellationToken cancellationToken)
    {
        if (hostCidrs.Count == 0)
            return;

        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var tableText = table.ToString();
        var installed = installedHosts.GetOrAdd(
            profile.Id,
            _ => new ConcurrentDictionary<string, long>(StringComparer.Ordinal));

        var pending = new List<string>();
        foreach (var cidr in hostCidrs)
        {
            var trimmed = PolicyRoutingCommands.NormalizeHostCidr(cidr);
            if (trimmed is null)
                continue;
            if (installed.ContainsKey(trimmed))
            {
                installed[trimmed] = Environment.TickCount64;
                continue;
            }

            if (!pending.Contains(trimmed, StringComparer.Ordinal))
                pending.Add(trimmed);
        }

        bulkRoutes.TryGetValue(profile.Id, out var staticRoutes);
        await EvictOverflowAsync(
                profile,
                installed,
                staticRoutes ?? [],
                pending.Count,
                tableText,
                cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return;

        var iface = await resolveInterface(profile.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(iface))
            return;

        var script = PolicyRoutingCommands.BuildAddHostRulesScript(pending, tableText);
        var result = await processRunner.RunPrivilegedShellAsync(script, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Policy routing {Profile}: failed to add {Count} monitored hosts: {Error}",
                profile.Name,
                pending.Count,
                result.StandardError.Trim());
            return;
        }

        foreach (var item in pending)
            installed[item] = Environment.TickCount64;

        logger.LogInformation(
            "Policy routing {Profile}: added {Count} monitored host routes via shared privileged session",
            profile.Name,
            pending.Count);
    }

    /// <summary>
    /// Ограничивает число динамических (от DNS-монитора) правил бюджетом <c>maxRoutes</c>:
    /// самые старые вытесняются, статические маршруты профиля не трогаются.
    /// </summary>
    private async Task EvictOverflowAsync(
        VpnProfile profile,
        ConcurrentDictionary<string, long> installed,
        HashSet<string> staticRoutes,
        int pendingCount,
        string tableText,
        CancellationToken cancellationToken)
    {
        var capacity = Math.Max(profile.SplitRouting.MaxRoutes, SplitRoutingSettings.MinMaxRoutes);
        var dynamicCount = installed.Count(pair => !staticRoutes.Contains(pair.Key));
        var overflow = dynamicCount + pendingCount - capacity;
        if (overflow <= 0)
            return;

        var victims = installed
            .Where(pair => !staticRoutes.Contains(pair.Key))
            .OrderBy(static pair => pair.Value)
            .Take(overflow)
            .Select(static pair => pair.Key)
            .ToList();
        if (victims.Count == 0)
            return;

        foreach (var victim in victims)
            installed.TryRemove(victim, out _);

        var result = await processRunner
            .RunPrivilegedShellAsync(
                PolicyRoutingCommands.BuildDeleteHostRulesScript(victims, tableText),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Policy routing {Profile}: evicted {Count} oldest monitored hosts (limit {Limit})",
                profile.Name,
                victims.Count,
                capacity);
            return;
        }

        logger.LogWarning(
            "Policy routing {Profile}: failed to evict {Count} monitored hosts: {Error}",
            profile.Name,
            victims.Count,
            result.StandardError.Trim());
    }

    /// <summary>
    /// Удаляет правила таблицы профиля, которых нет в актуальном наборе. Без этого
    /// правила, добавленные DNS-монитором и прошлыми запусками, копятся до тысяч и
    /// начинают заворачивать в туннель чужие адреса (например, точки входа tuna).
    /// </summary>
    public async Task<int> PruneDestinationRulesAsync(
        VpnProfile profile,
        IReadOnlyCollection<string> keepCidrs,
        CancellationToken cancellationToken)
    {
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var keep = keepCidrs as HashSet<string> ?? new HashSet<string>(keepCidrs, StringComparer.Ordinal);

        var stale = new List<string>();
        stale.AddRange(await CollectStaleRulesAsync(["rule", "list"], table, keep, cancellationToken)
            .ConfigureAwait(false));
        stale.AddRange(await CollectStaleRulesAsync(["-6", "rule", "list"], table, keep, cancellationToken)
            .ConfigureAwait(false));

        if (stale.Count == 0)
            return 0;

        var tableText = table.ToString();
        foreach (var cidr in stale)
        {
            string[] arguments = cidr.Contains(':', StringComparison.Ordinal)
                ? ["-6", "rule", "del", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", cidr, "lookup", tableText]
                : ["rule", "del", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", cidr, "lookup", tableText];
            await RunIpPrivilegedIgnoringErrorsAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Policy routing {Profile}: pruned {Count} stale destination rules",
            profile.Name,
            stale.Count);
        return stale.Count;
    }

    private async Task<List<string>> CollectStaleRulesAsync(
        string[] listArguments,
        int table,
        HashSet<string> keep,
        CancellationToken cancellationToken)
    {
        var listed = await processRunner.RunAsync("ip", listArguments, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
            return [];

        var stale = new List<string>();
        foreach (var line in listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseManagedRule(line, table, out var cidr))
                continue;
            if (keep.Contains(cidr))
                continue;

            stale.Add(cidr);
        }

        return stale;
    }

    internal static bool TryParseManagedRule(string line, int table, out string cidr)
    {
        cidr = string.Empty;
        var trimmed = line.Trim();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
            return false;
        if (!int.TryParse(trimmed[..colon], out var preference) || preference != PolicyRoutingCommands.RulePreference)
            return false;

        var tokens = trimmed[(colon + 1)..]
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? destination = null;
        string? lookupTable = null;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (string.Equals(tokens[i], "to", StringComparison.Ordinal))
                destination = tokens[i + 1];
            else if (string.Equals(tokens[i], "lookup", StringComparison.Ordinal))
                lookupTable = tokens[i + 1];
        }

        if (destination is null || lookupTable is null)
            return false;
        if (string.Equals(destination, "all", StringComparison.Ordinal))
            return false;
        if (!int.TryParse(lookupTable, out var parsedTable) || parsedTable != table)
            return false;

        cidr = destination;
        return true;
    }

    public async Task ClearRulesForTableAsync(int table, CancellationToken cancellationToken)
    {
        var tableText = table.ToString();
        await RunIpPrivilegedIgnoringErrorsAsync(["rule", "flush", "table", tableText], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(["-6", "rule", "flush", "table", tableText], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EnsurePolicyTableDefaultRouteAsync(
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

    public async Task CleanupOrphanPolicyTablesAsync(VpnProfile profile, CancellationToken cancellationToken)
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
            if (!trimmed.StartsWith($"{PolicyRoutingCommands.RulePreference}:", StringComparison.Ordinal))
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

    public async Task<bool> IsInterfaceIpv6CapableAsync(string iface, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "ip",
            ["-6", "addr", "show", "dev", iface],
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            return false;

        return result.StandardOutput.Contains("inet6 ", StringComparison.Ordinal);
    }

    private async Task MergeDestinationRulesAsync(
        string profileId,
        IReadOnlyList<string> ipv4Routes,
        IReadOnlyList<string> ipv6Routes,
        string tableText,
        string iface,
        bool ipv6Capable,
        bool readdAll,
        IReadOnlyDictionary<string, HashSet<string>> bulkRoutes,
        CancellationToken cancellationToken)
    {
        var desired = PolicyRoutingCommands.NormalizeRouteSet(ipv4Routes.Concat(ipv6Capable ? ipv6Routes : []));
        bulkRoutes.TryGetValue(profileId, out var previous);
        previous ??= [];

        foreach (var route in desired)
        {
            if (!readdAll && previous.Contains(route))
                continue;

            if (route.Contains(':', StringComparison.Ordinal))
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["-6", "rule", "add", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["rule", "add", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var route in previous)
        {
            if (desired.Contains(route))
                continue;

            if (PolicyRoutingCommands.NormalizeHostCidr(route) is not null)
                continue;

            if (route.Contains(':', StringComparison.Ordinal))
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["-6", "rule", "del", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", route, "lookup", tableText],
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunIpPrivilegedIgnoringErrorsAsync(
                    ["rule", "del", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", route, "lookup", tableText],
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
                ["rule", "add", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", trimmed, "lookup", tableText],
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
                ["-6", "rule", "add", "pref", PolicyRoutingCommands.RulePreference.ToString(), "to", trimmed, "lookup", tableText],
                cancellationToken).ConfigureAwait(false);
        }

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["-6", "route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunIpPrivilegedAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.StandardError.Trim());
    }

    private Task RunIpPrivilegedIgnoringErrorsAsync(string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken);
}