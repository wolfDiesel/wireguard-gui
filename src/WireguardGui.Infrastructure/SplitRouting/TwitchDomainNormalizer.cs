using System.Text.RegularExpressions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

internal static partial class TwitchDomainNormalizer
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string> rawDomains)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawDomains)
        {
            var normalized = NormalizeOne(raw);
            if (normalized is not null)
                result.Add(normalized);
        }

        return result.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static string? NormalizeOne(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var domain = raw.Trim().ToLowerInvariant();

        if (domain.StartsWith("*.", StringComparison.Ordinal))
            domain = domain[2..];

        var starIndex = domain.IndexOf('*');
        if (starIndex >= 0)
        {
            var suffixStart = domain.IndexOf('.', starIndex);
            domain = suffixStart >= 0 && suffixStart < domain.Length - 1
                ? domain[(suffixStart + 1)..]
                : domain.Replace("*", string.Empty, StringComparison.Ordinal).Trim('.');
        }

        return DomainPattern().IsMatch(domain) ? domain : null;
    }

    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainPattern();
}
