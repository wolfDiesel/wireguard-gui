using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class ProfileConfigDnsSync(
    IProfileStore profileStore,
    IWireGuardConfigParser configParser) : IProfileConfigDnsSync
{
    public async Task<OperationResultDto> SyncTunnelDnsAsync(
        VpnProfile profile,
        CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        if (!File.Exists(configPath))
            return new OperationResultDto(false, OperationErrorCode.ConfigNotFound, "Configuration file not found");

        var configContent = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var updated = configParser.ApplyTunnelDns(
            configContent,
            TunnelDnsServers.ResolveOrDefault(profile.SplitRouting.TunnelDns));
        if (string.Equals(updated, configContent, StringComparison.Ordinal))
            return new OperationResultDto(true);

        await File.WriteAllTextAsync(configPath, updated, cancellationToken).ConfigureAwait(false);
        return new OperationResultDto(true);
    }
}
