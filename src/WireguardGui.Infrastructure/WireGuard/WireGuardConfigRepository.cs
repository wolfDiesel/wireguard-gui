using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

/// <summary>
/// Единая точка доступа к файлу wireguard.conf профиля.
/// Гарантирует атомарность чтения/записи (lock) и устраняет гонки
/// между параллельными apply + save (см. анализ, пункт 6).
/// </summary>
public sealed class WireGuardConfigRepository(
    IProfileStore profileStore,
    IWireGuardConfigParser configParser) : IWireGuardConfigRepository
{
    private readonly object _gate = new();

    public async Task<string> ReadAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found", configPath);

        lock (_gate)
        {
            return File.ReadAllText(configPath);
        }
    }

    public async Task WriteAsync(
        VpnProfile profile,
        string content,
        CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        lock (_gate)
        {
            File.WriteAllText(configPath, content);
        }
    }

    public async Task<string> UpdateAsync(
        VpnProfile profile,
        Func<string, string> transform,
        CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found", configPath);

        lock (_gate)
        {
            var current = File.ReadAllText(configPath);
            var updated = transform(current);
            if (!string.Equals(updated, current, StringComparison.Ordinal))
                File.WriteAllText(configPath, updated);
            return updated;
        }
    }
}