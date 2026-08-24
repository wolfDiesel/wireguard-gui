using System.Text.Json;
using System.Text.RegularExpressions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

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
        if (TableLinePattern().IsMatch(configContent))
            return TableLinePattern().Replace(configContent, $"Table = {tableValue}\n");

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
        if (!HasDns(updated))
            updated = WriteDns(updated, TunnelDnsServers.Default);

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
        if (AllowedIpsLinePattern().IsMatch(configContent))
            return AllowedIpsLinePattern().Replace(configContent, $"AllowedIPs = {allowedIpsCsv}\n");

        var peerIndex = configContent.IndexOf("[Peer]", StringComparison.Ordinal);
        if (peerIndex < 0)
            throw new WireGuardConfigValidationException("[Peer] section not found");

        var insertAt = configContent.IndexOf('\n', peerIndex);
        if (insertAt < 0)
            insertAt = configContent.Length;

        return configContent.Insert(insertAt + 1, $"AllowedIPs = {allowedIpsCsv}\n");
    }

    public bool HasDns(string configContent) => DnsPattern().IsMatch(configContent);

    public IReadOnlyList<string> ReadDnsServers(string configContent)
    {
        var match = DnsValuePattern().Match(configContent);
        if (!match.Success)
            return [];

        return match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> ResolveTunnelDnsServers(string configContent)
    {
        var servers = ReadDnsServers(configContent);
        if (servers.Count > 0)
            return servers;

        return [TunnelDnsServers.Default];
    }

    public string RemoveDns(string configContent) => DnsPattern().Replace(configContent, string.Empty);

    public string WriteDns(string configContent, string dnsCsv)
    {
        if (DnsPattern().IsMatch(configContent))
            return DnsPattern().Replace(configContent, $"DNS = {dnsCsv}\n");

        var interfaceIndex = configContent.IndexOf("[Interface]", StringComparison.Ordinal);
        if (interfaceIndex < 0)
            return configContent;

        var insertAt = configContent.IndexOf('\n', interfaceIndex);
        if (insertAt < 0)
            insertAt = configContent.Length;

        return configContent.Insert(insertAt + 1, $"DNS = {dnsCsv}\n");
    }

    public string ApplyTunnelDns(string configContent, string? tunnelDnsCsv) =>
        string.IsNullOrWhiteSpace(tunnelDnsCsv)
            ? RemoveDns(configContent)
            : WriteDns(configContent, tunnelDnsCsv);

    public string? InferDnsFromAddress(string configContent) => InferDnsFromAddressStatic(configContent);

    internal static string? InferDnsFromAddressStatic(string configContent)
    {
        var match = AddressPattern().Match(configContent);
        if (!match.Success)
            return null;

        var address = match.Groups[1].Value.Trim();
        var slash = address.IndexOf('/');
        var host = slash >= 0 ? address[..slash] : address;
        if (!global::System.Net.IPAddress.TryParse(host, out var ip) ||
            ip.AddressFamily != global::System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        var bytes = ip.GetAddressBytes();
        bytes[^1] = 1;
        return new global::System.Net.IPAddress(bytes).ToString();
    }

    public string NormalizeAllowedIps(string allowedIpsCsv) =>
        string.Join(
            ",",
            allowedIpsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(static ip => ip, StringComparer.Ordinal));

    [GeneratedRegex(@"AllowedIPs\s*=\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedIpsPattern();

    [GeneratedRegex(@"^\s*AllowedIPs\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AllowedIpsLinePattern();

    [GeneratedRegex(@"^\s*Table\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TablePattern();

    [GeneratedRegex(@"^\s*Table\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TableLinePattern();

    [GeneratedRegex(@"^\s*Endpoint\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex EndpointPattern();

    [GeneratedRegex(@"^\s*DNS\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DnsPattern();

    [GeneratedRegex(@"^\s*DNS\s*=\s*(.+?)\s*(?:#.*)?(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DnsValuePattern();

    [GeneratedRegex(@"^\s*Address\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex AddressPattern();

    [GeneratedRegex(@"\[Interface\][\s\S]*?^Name\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InterfaceNamePattern();

    [GeneratedRegex(@"^\s*Name\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InterfaceNameLinePattern();
}
