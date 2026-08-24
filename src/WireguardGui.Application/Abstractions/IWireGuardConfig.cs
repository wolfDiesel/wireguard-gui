using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IWireGuardConfigValidator
{
    void Validate(string configContent);
}

public interface IWireGuardConfigParser
{
    string? ReadInterfaceName(string configContent);
    string RemoveInterfaceName(string configContent);
    string ReadAllowedIps(string configContent);
    string? ReadRoutingTable(string configContent);
    string ReadEndpointHost(string configContent);
    IReadOnlyList<string> ReadDnsServers(string configContent);
    IReadOnlyList<string> ResolveTunnelDnsServers(string configContent);
    bool HasDns(string configContent);
    string WriteAllowedIps(string configContent, string allowedIpsCsv);
    string WriteRoutingTable(string configContent, string tableValue);
    string EnsurePolicySplitBaseline(string configContent);
    bool IsPolicySplitBaseline(string configContent);
    string RemoveDns(string configContent);
    string WriteDns(string configContent, string dnsCsv);
    string ApplyTunnelDns(string configContent, string? tunnelDnsCsv);
    string? InferDnsFromAddress(string configContent);
    string NormalizeAllowedIps(string allowedIpsCsv);
}

public interface IProfileConfigDnsSync
{
    Task<OperationResultDto> SyncTunnelDnsAsync(VpnProfile profile, CancellationToken cancellationToken = default);
}
