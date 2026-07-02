using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class NativeWireGuardBackend(
    IProcessRunner processRunner,
    IProfileStore profileStore) : IWireGuardBackend
{
    public BackendKind Kind => BackendKind.Native;

    public async Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var result = await processRunner.RunPrivilegedAsync(
            "wg-quick",
            ["up", configPath],
            cancellationToken);

        if (!result.IsSuccess)
            throw new WireGuardOperationException("Failed to bring up tunnel", result.StandardError.Trim());
    }

    public async Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var result = await processRunner.RunPrivilegedAsync(
            "wg-quick",
            ["down", configPath],
            cancellationToken);

        if (!result.IsSuccess && !result.StandardError.Contains("is not a WireGuard interface", StringComparison.OrdinalIgnoreCase))
            throw new WireGuardOperationException("Failed to bring down tunnel", result.StandardError.Trim());
    }

    public async Task<ConnectionState> GetConnectionStateAsync(
        VpnProfile profile,
        CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync("wg", ["show"], cancellationToken);
        if (!result.IsSuccess)
            return ConnectionState.Unknown;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("interface:", StringComparison.OrdinalIgnoreCase))
                continue;

            var iface = line["interface:".Length..].Trim();
            if (string.Equals(iface, profile.ConnectionName, StringComparison.Ordinal))
                return ConnectionState.Connected;
        }

        return ConnectionState.Disconnected;
    }

    public async Task ReimportFromConfigAsync(
        VpnProfile profile,
        bool connectAfter,
        CancellationToken cancellationToken = default)
    {
        var wasConnected = await GetConnectionStateAsync(profile, cancellationToken) == ConnectionState.Connected;
        if (!wasConnected && !connectAfter)
            return;

        if (wasConnected)
            await DisconnectAsync(profile, cancellationToken);

        if (connectAfter || wasConnected)
            await ConnectAsync(profile, cancellationToken);
    }

    public Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
        DisconnectAsync(profile, cancellationToken);
}
