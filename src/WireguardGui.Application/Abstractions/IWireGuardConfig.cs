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
    bool HasDns(string configContent);
    string WriteAllowedIps(string configContent, string allowedIpsCsv);
    string RemoveDns(string configContent);
    string NormalizeAllowedIps(string allowedIpsCsv);
}
