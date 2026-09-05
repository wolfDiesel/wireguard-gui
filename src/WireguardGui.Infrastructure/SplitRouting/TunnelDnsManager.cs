using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class TunnelDnsManager(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    ILogger<TunnelDnsManager> logger)
{
    public async Task EnsureTunnelDnsAsync(
        VpnProfile profile,
        string iface,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> installedHosts,
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
        await EnsureDnsPolicyRoutesAsync(profile, iface, dnsServers, installedHosts, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Policy routing {Profile}: tunnel DNS {Servers} on {Interface}",
            profile.Name,
            string.Join(", ", dnsServers),
            iface);
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

    private async Task EnsureDnsPolicyRoutesAsync(
        VpnProfile profile,
        string iface,
        IReadOnlyList<string> dnsServers,
        ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> installedHosts,
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
                .GetOrAdd(profile.Id, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
                [cidr] = 0;
        }

        await RunIpPrivilegedIgnoringErrorsAsync(
            ["route", "replace", "default", "dev", iface, "table", tableText],
            cancellationToken).ConfigureAwait(false);
    }

    private Task RunIpPrivilegedIgnoringErrorsAsync(string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken);

    private Task RunPrivilegedIgnoringErrorsAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync(fileName, arguments, cancellationToken);
}