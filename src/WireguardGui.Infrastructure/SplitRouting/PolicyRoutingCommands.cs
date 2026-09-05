using System.Net;
using System.Net.Sockets;
using System.Text;
using WireguardGui.Application.Abstractions;

namespace WireguardGui.Infrastructure.SplitRouting;

internal static class PolicyRoutingCommands
{
    public const int RulePreference = 100;

    public static string ShellEscape(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public static string? NormalizeHostCidr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (trimmed.EndsWith("/128", StringComparison.Ordinal))
                return IPAddress.TryParse(trimmed[..^4], out _) ? trimmed : null;
            if (IPAddress.TryParse(trimmed, out var ip6) && ip6.AddressFamily == AddressFamily.InterNetworkV6)
                return ip6 + "/128";
            return null;
        }

        if (trimmed.EndsWith("/32", StringComparison.Ordinal))
            return IPAddress.TryParse(trimmed[..^3], out _) ? trimmed : null;
        if (IPAddress.TryParse(trimmed, out var ip4) && ip4.AddressFamily == AddressFamily.InterNetwork)
            return ip4 + "/32";
        return null;
    }

    public static HashSet<string> NormalizeRouteSet(IEnumerable<string> routes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            var trimmed = route.Trim();
            if (trimmed.Length > 0)
                set.Add(trimmed);
        }

        return set;
    }

    public static string BuildAddHostRulesScript(IReadOnlyList<string> cidrs, string tableText)
    {
        var builder = new StringBuilder();
        foreach (var cidr in cidrs)
        {
            if (cidr.Contains(':', StringComparison.Ordinal))
            {
                builder.Append("ip -6 rule add pref ")
                    .Append(RulePreference)
                    .Append(" to ")
                    .Append(ShellEscape(cidr))
                    .Append(" lookup ")
                    .Append(tableText)
                    .Append(" 2>/dev/null || true\n");
            }
            else
            {
                builder.Append("ip rule add pref ")
                    .Append(RulePreference)
                    .Append(" to ")
                    .Append(ShellEscape(cidr))
                    .Append(" lookup ")
                    .Append(tableText)
                    .Append(" 2>/dev/null || true\n");
            }
        }

        return builder.ToString();
    }

    public static string FormatFwMark(uint mark) => $"0x{mark:x}";
}