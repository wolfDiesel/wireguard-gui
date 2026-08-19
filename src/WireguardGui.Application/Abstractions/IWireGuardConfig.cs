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
    bool HasDns(string configContent);
    string WriteAllowedIps(string configContent, string allowedIpsCsv);
    string WriteRoutingTable(string configContent, string tableValue);
    string EnsurePolicySplitBaseline(string configContent);
    bool IsPolicySplitBaseline(string configContent);
    string RemoveDns(string configContent);
    string NormalizeAllowedIps(string allowedIpsCsv);
}
