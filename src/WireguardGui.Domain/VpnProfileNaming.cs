using System.Text;

namespace WireguardGui.Domain;

public static class VpnProfileNaming
{
    public const int AmneziaWgInterfaceNameMaxLength = 15;

    public static bool IsValidConnectionName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 64
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    public static bool IsValidAmneziaWgInterfaceName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= AmneziaWgInterfaceNameMaxLength
        && name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '=' or '+' or '.' or '-');

    public static string ToAmneziaWgInterfaceName(string? candidate)
    {
        var builder = new StringBuilder(AmneziaWgInterfaceNameMaxLength);
        foreach (var c in candidate ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '=' or '+' or '.')
            {
                builder.Append(c);
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var name = builder.ToString().Trim('-');
        if (name.Length > AmneziaWgInterfaceNameMaxLength)
            name = name[..AmneziaWgInterfaceNameMaxLength].TrimEnd('-');

        return name.Length == 0 ? "awg" : name;
    }

    public static string MakeUniqueAmneziaWgInterfaceName(
        string? candidate,
        IReadOnlyCollection<string> taken)
    {
        var baseName = ToAmneziaWgInterfaceName(candidate);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName))
            return baseName;

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (suffixText.Length >= AmneziaWgInterfaceNameMaxLength)
                return baseName;

            var headLength = Math.Min(baseName.Length, AmneziaWgInterfaceNameMaxLength - suffixText.Length);
            var name = baseName[..headLength] + suffixText;
            if (!used.Contains(name))
                return name;
        }
    }
}
