using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.Storage;

public sealed class ConfigKindProbe(IWireGuardConfigParser configParser) : IConfigKindProbe
{
    public async Task<ConfigKind> DetectAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return ConfigKind.WireGuard;

        var content = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        return configParser.IsAmneziaWg(content) ? ConfigKind.AmneziaWg : ConfigKind.WireGuard;
    }
}
