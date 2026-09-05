using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class EndpointRouteGuard(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    ILogger<EndpointRouteGuard> logger)
{
    public async Task EnsureEndpointRouteAsync(VpnProfile profile, CancellationToken cancellationToken)
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

        var wgInterfaces = await GetWireGuardInterfacesAsync(cancellationToken).ConfigureAwait(false);
        var currentIface = await ResolveWireGuardInterfaceAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(currentIface))
            wgInterfaces.Add(currentIface);

        var gatewayLines = gatewayResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var gatewayLine = gatewayLines.FirstOrDefault(line => !IsWireGuardRoute(line, wgInterfaces));
        if (gatewayLine is null)
        {
            logger.LogWarning(
                "No non-WireGuard default route candidate for endpoint {Endpoint}; all default routes go through wg interfaces. Full output: {Output}",
                endpointHost,
                gatewayResult.StandardOutput.Trim());
            return;
        }

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

    public async Task<string?> ResolveWireGuardInterfaceAsync(
        VpnProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Backend == BackendKind.Nmcli && processRunner.IsCommandAvailable("nmcli"))
        {
            var nmResult = await processRunner.RunAsync(
                "nmcli",
                ["-g", "connection.interface-name", "connection", "show", profile.ConnectionName],
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

    private async Task<HashSet<string>> GetWireGuardInterfacesAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("wg", ["show", "interfaces"], cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return [];

        return result.StandardOutput
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsWireGuardRoute(string line, IReadOnlySet<string> wgInterfaces)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var devIndex = Array.IndexOf(parts, "dev");
        if (devIndex < 0 || devIndex + 1 >= parts.Length)
            return false;
        return wgInterfaces.Contains(parts[devIndex + 1]);
    }

    private async Task RunIpPrivilegedAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.StandardError.Trim());
    }
}