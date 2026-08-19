using System.Text;
using System.Text.RegularExpressions;

namespace WireguardGui.Infrastructure.SplitRouting;

internal static partial class PolicyRoutingNaming
{
    public const string NftTable = "wireguard_gui";

    public static string Sanitize(string profileId) =>
        SanitizePattern().Replace(profileId, "_");

    public static string HostsSetName(string profileId) => $"hosts_{Sanitize(profileId)}";

    public static string NetsSetName(string profileId) => $"nets_{Sanitize(profileId)}";

    public static string Hosts6SetName(string profileId) => $"hosts6_{Sanitize(profileId)}";

    public static string ChainName(string profileId) => $"split_{Sanitize(profileId)}";

    public static int RoutingTableId(string profileId) =>
        100 + Math.Abs(profileId.GetHashCode(StringComparison.Ordinal)) % 800;

    public static uint FwMark(string profileId) =>
        (uint)(0x77770000 | (Math.Abs(profileId.GetHashCode(StringComparison.Ordinal)) & 0xffff));

    public static string FormatNftElements(IReadOnlyList<string> routes)
    {
        if (routes.Count == 0)
            return "{ }";

        var builder = new StringBuilder("{ ");
        for (var i = 0; i < routes.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(routes[i]);
        }

        builder.Append(" }");
        return builder.ToString();
    }

    public static (IReadOnlyList<string> Hosts, IReadOnlyList<string> Nets, IReadOnlyList<string> Hosts6) ClassifyRoutes(
        IReadOnlyList<string> routes)
    {
        var hosts = new List<string>();
        var nets = new List<string>();
        var hosts6 = new List<string>();

        foreach (var route in routes)
        {
            var trimmed = route.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed.EndsWith("/128", StringComparison.Ordinal))
                hosts6.Add(trimmed[..^4]);
            else if (trimmed.EndsWith("/32", StringComparison.Ordinal))
                hosts.Add(trimmed[..^3]);
            else if (trimmed.Contains('/', StringComparison.Ordinal))
                nets.Add(trimmed);
            else if (trimmed.Contains(':', StringComparison.Ordinal))
                hosts6.Add(trimmed);
            else
                hosts.Add(trimmed);
        }

        return (hosts, nets, hosts6);
    }

    public static string NormalizeRoutesKey(IReadOnlyList<string> routes) =>
        string.Join(",", routes.OrderBy(static r => r, StringComparer.Ordinal));

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitizePattern();
}
