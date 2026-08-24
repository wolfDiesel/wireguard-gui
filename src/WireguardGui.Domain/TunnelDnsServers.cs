using System.Net;
using System.Net.Sockets;

namespace WireguardGui.Domain;

public static class TunnelDnsServers
{
    public const string Default = "8.8.8.8";

    public static bool TryParse(string? text, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        var servers = text
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => value.Length > 0)
            .ToList();

        if (servers.Count == 0)
            return true;

        var unique = new List<string>(servers.Count);
        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                error = $"Invalid DNS server: {server}";
                return false;
            }

            var formatted = ip.ToString();
            if (!unique.Contains(formatted, StringComparer.OrdinalIgnoreCase))
                unique.Add(formatted);
        }

        normalized = string.Join(", ", unique);
        return true;
    }

    public static string ResolveOrDefault(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Default;

        return TryParse(text, out var normalized, out _) && !string.IsNullOrWhiteSpace(normalized)
            ? normalized!
            : Default;
    }

    public static bool IsTunnelLocal(string server)
    {
        if (!IPAddress.TryParse(server, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
    }

    public static bool ShouldCaptureAllDns(IReadOnlyList<string> servers) => servers.Count > 0;
}
