using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.System;

public sealed class OsReleaseDistroProbe(
    IProcessRunner processRunner,
    string osReleasePath = "/etc/os-release") : IDistroProbe
{
    public async Task<DistroFamily> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(osReleasePath))
        {
            var content = await File.ReadAllTextAsync(osReleasePath, cancellationToken).ConfigureAwait(false);
            var parsed = Parse(content);
            if (parsed != DistroFamily.Unknown)
                return parsed;
        }

        return DetectByPackageManager();
    }

    internal static DistroFamily Parse(string osReleaseContent)
    {
        var values = ReadValues(osReleaseContent);

        if (values.TryGetValue("ID", out var id))
        {
            var family = MapId(id);
            if (family != DistroFamily.Unknown)
                return family;
        }

        if (values.TryGetValue("ID_LIKE", out var idLike))
        {
            foreach (var token in idLike.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var family = MapId(token);
                if (family != DistroFamily.Unknown)
                    return family;
            }
        }

        return DistroFamily.Unknown;
    }

    internal static DistroFamily MapId(string id) =>
        id.Trim().ToLowerInvariant() switch
        {
            "debian" or "ubuntu" or "linuxmint" or "pop" or "raspbian" or "kali" or "elementary" or "zorin"
                => DistroFamily.Debian,
            "fedora" => DistroFamily.Fedora,
            "rhel" or "centos" or "rocky" or "almalinux" or "ol" => DistroFamily.Rhel,
            "altlinux" or "alt" => DistroFamily.Alt,
            _ => DistroFamily.Unknown,
        };

    private static Dictionary<string, string> ReadValues(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
            values[key] = value;
        }

        return values;
    }

    private DistroFamily DetectByPackageManager()
    {
        if (processRunner.IsCommandAvailable("dnf"))
            return DistroFamily.Rhel;
        if (processRunner.IsCommandAvailable("apt-get"))
            return DistroFamily.Debian;
        return DistroFamily.Unknown;
    }
}
