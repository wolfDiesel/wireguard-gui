using System.Text.Json;
using System.Text.RegularExpressions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed partial class WireGuardConfigValidator : IWireGuardConfigValidator
{
    public void Validate(string configContent)
    {
        if (string.IsNullOrWhiteSpace(configContent))
            throw new WireGuardConfigValidationException("Config is empty");

        if (!configContent.Contains("[Interface]", StringComparison.Ordinal))
            throw new WireGuardConfigValidationException("Missing [Interface] section");

        if (!configContent.Contains("[Peer]", StringComparison.Ordinal))
            throw new WireGuardConfigValidationException("Missing [Peer] section");

        if (!PrivateKeyPattern().IsMatch(configContent))
            throw new WireGuardConfigValidationException("PrivateKey not found in [Interface]");

        if (!PublicKeyPattern().IsMatch(configContent))
            throw new WireGuardConfigValidationException("PublicKey not found in [Peer]");
    }

    [GeneratedRegex(@"PrivateKey\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(@"PublicKey\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex PublicKeyPattern();
}

public sealed partial class WireGuardConfigParser : IWireGuardConfigParser
{
    public string? ReadInterfaceName(string configContent)
    {
        var match = InterfaceNamePattern().Match(configContent);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public string RemoveInterfaceName(string configContent) =>
        InterfaceNameLinePattern().Replace(configContent, string.Empty);

    public string ReadAllowedIps(string configContent)
    {
        var match = AllowedIpsPattern().Match(configContent);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    public string? ReadRoutingTable(string configContent)
    {
        var match = TablePattern().Match(configContent);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public string ReadEndpointHost(string configContent)
    {
        var match = EndpointPattern().Match(configContent);
        if (!match.Success)
            return string.Empty;

        var endpoint = match.Groups[1].Value.Trim();
        var colon = endpoint.LastIndexOf(':');
        if (colon <= 0)
            return endpoint;

        var host = endpoint[..colon];
        return host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
    }

    public string WriteRoutingTable(string configContent, string tableValue)
    {
        if (TablePattern().IsMatch(configContent))
            return TablePattern().Replace(configContent, $"Table = {tableValue}");

        var interfaceIndex = configContent.IndexOf("[Interface]", StringComparison.Ordinal);
        if (interfaceIndex < 0)
            throw new WireGuardConfigValidationException("[Interface] section not found");

        var insertAt = configContent.IndexOf('\n', interfaceIndex);
        if (insertAt < 0)
            insertAt = configContent.Length;

        return configContent.Insert(insertAt + 1, $"Table = {tableValue}\n");
    }

    public string EnsurePolicySplitBaseline(string configContent)
    {
        var updated = WriteRoutingTable(configContent, SplitRoutingPolicy.PolicyTable);
        updated = WriteAllowedIps(updated, SplitRoutingPolicy.PolicyAllowedIps);
        if (HasDns(updated) && SplitRoutingPolicy.RemoveDnsOnApply)
            updated = RemoveDns(updated);
        return updated;
    }

    public bool IsPolicySplitBaseline(string configContent)
    {
        var table = ReadRoutingTable(configContent);
        var allowed = NormalizeAllowedIps(ReadAllowedIps(configContent));
        return string.Equals(table, SplitRoutingPolicy.PolicyTable, StringComparison.OrdinalIgnoreCase)
            && allowed == SplitRoutingPolicy.PolicyAllowedIps;
    }

    public string WriteAllowedIps(string configContent, string allowedIpsCsv)
    {
        if (AllowedIpsPattern().IsMatch(configContent))
            return AllowedIpsPattern().Replace(configContent, $"AllowedIPs = {allowedIpsCsv}");

        var peerIndex = configContent.IndexOf("[Peer]", StringComparison.Ordinal);
        if (peerIndex < 0)
            throw new WireGuardConfigValidationException("[Peer] section not found");

        var insertAt = configContent.IndexOf('\n', peerIndex);
        if (insertAt < 0)
            insertAt = configContent.Length;

        return configContent.Insert(insertAt + 1, $"AllowedIPs = {allowedIpsCsv}\n");
    }

    public bool HasDns(string configContent) => DnsPattern().IsMatch(configContent);

    public string RemoveDns(string configContent) => DnsPattern().Replace(configContent, string.Empty);

    public string NormalizeAllowedIps(string allowedIpsCsv) =>
        string.Join(
            ",",
            allowedIpsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(static ip => ip, StringComparer.Ordinal));

    [GeneratedRegex(@"AllowedIPs\s*=\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedIpsPattern();

    [GeneratedRegex(@"^\s*Table\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TablePattern();

    [GeneratedRegex(@"^\s*Endpoint\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex EndpointPattern();

    [GeneratedRegex(@"^\s*DNS\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DnsPattern();

    [GeneratedRegex(@"\[Interface\][\s\S]*?^Name\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InterfaceNamePattern();

    [GeneratedRegex(@"^\s*Name\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InterfaceNameLinePattern();
}
