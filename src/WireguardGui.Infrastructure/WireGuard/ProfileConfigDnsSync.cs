using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class ProfileConfigDnsSync(
    IWireGuardConfigRepository configRepository,
    IWireGuardConfigParser configParser) : IProfileConfigDnsSync
{
    public async Task<OperationResultDto> SyncTunnelDnsAsync(
        VpnProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configContent = await configRepository.ReadAsync(profile, cancellationToken).ConfigureAwait(false);
            var updated = configParser.ApplyTunnelDns(
                configContent,
                TunnelDnsServers.ResolveOrDefault(profile.SplitRouting.TunnelDns));
            if (string.Equals(updated, configContent, StringComparison.Ordinal))
                return new OperationResultDto(true);

            await configRepository.WriteAsync(profile, updated, cancellationToken).ConfigureAwait(false);
            return new OperationResultDto(true);
        }
        catch (FileNotFoundException)
        {
            return new OperationResultDto(false, OperationErrorCode.ConfigNotFound, "Configuration file not found");
        }
    }
}
