using System.Text.Json;
using WireguardGui.Application.Abstractions;

namespace WireguardGui.Infrastructure.SplitRouting;

internal sealed class TwitchStreamHostCache(IAppDataPaths appDataPaths)
{
    private string CachePath => Path.Combine(appDataPaths.DataRoot, "twitch-stream-hosts-cache.json");

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CachePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(CachePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<string> hosts, CancellationToken cancellationToken)
    {
        var list = hosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
            return;

        Directory.CreateDirectory(appDataPaths.DataRoot);
        await File.WriteAllTextAsync(
            CachePath,
            JsonSerializer.Serialize(list),
            cancellationToken).ConfigureAwait(false);
    }
}
