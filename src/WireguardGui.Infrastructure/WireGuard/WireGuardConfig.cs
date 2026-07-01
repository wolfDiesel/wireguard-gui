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
            throw new WireGuardConfigValidationException("Конфиг пуст");

        if (!configContent.Contains("[Interface]", StringComparison.Ordinal))
            throw new WireGuardConfigValidationException("Отсутствует секция [Interface]");

        if (!configContent.Contains("[Peer]", StringComparison.Ordinal))
            throw new WireGuardConfigValidationException("Отсутствует секция [Peer]");

        if (!PrivateKeyPattern().IsMatch(configContent))
            throw new WireGuardConfigValidationException("Не найден PrivateKey в [Interface]");

        if (!PublicKeyPattern().IsMatch(configContent))
            throw new WireGuardConfigValidationException("Не найден PublicKey в [Peer]");
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

    public string WriteAllowedIps(string configContent, string allowedIpsCsv)
    {
        if (AllowedIpsPattern().IsMatch(configContent))
            return AllowedIpsPattern().Replace(configContent, $"AllowedIPs = {allowedIpsCsv}");

        var peerIndex = configContent.IndexOf("[Peer]", StringComparison.Ordinal);
        if (peerIndex < 0)
            throw new WireGuardConfigValidationException("Секция [Peer] не найдена");

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

    [GeneratedRegex(@"^\s*DNS\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DnsPattern();

    [GeneratedRegex(@"\[Interface\][\s\S]*?^Name\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InterfaceNamePattern();

    [GeneratedRegex(@"^\s*Name\s*=.*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InterfaceNameLinePattern();
}
