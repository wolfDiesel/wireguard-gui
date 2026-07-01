using System.Text.Json;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.Storage;

public sealed class JsonProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string DataRoot { get; }

    public JsonProfileStore()
        : this(GetDefaultDataRoot())
    {
    }

    public JsonProfileStore(string dataRoot) => DataRoot = dataRoot;

    public async Task<IReadOnlyList<VpnProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profilesDir = Path.Combine(DataRoot, "profiles");
        if (!Directory.Exists(profilesDir))
            return [];

        var profiles = new List<VpnProfile>();
        foreach (var dir in Directory.GetDirectories(profilesDir))
        {
            var profileFile = Path.Combine(dir, "profile.json");
            if (!File.Exists(profileFile))
                continue;

            var json = await File.ReadAllTextAsync(profileFile, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<ProfileFile>(json, JsonOptions);
            if (file is not null)
                profiles.Add(await NormalizeProfileAsync(file.ToDomain(), dir, cancellationToken));
        }

        return profiles.OrderBy(p => p.Name).ToList();
    }

    public async Task<VpnProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profileFile = Path.Combine(GetProfileDirectory(profileId), "profile.json");
        if (!File.Exists(profileFile))
            return null;

        var json = await File.ReadAllTextAsync(profileFile, cancellationToken).ConfigureAwait(false);
        var file = JsonSerializer.Deserialize<ProfileFile>(json, JsonOptions);
        if (file is null)
            return null;

        return await NormalizeProfileAsync(file.ToDomain(), GetProfileDirectory(profileId), cancellationToken);
    }

    public async Task SaveProfileAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var dir = GetProfileDirectory(profile.Id);
        Directory.CreateDirectory(dir);

        var profileFile = Path.Combine(dir, "profile.json");
        await using var stream = File.Create(profileFile);
        await JsonSerializer.SerializeAsync(stream, ProfileFile.FromDomain(profile), JsonOptions, cancellationToken);
    }

    public Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var dir = GetProfileDirectory(profileId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    public string GetProfileDirectory(string profileId) =>
        Path.Combine(DataRoot, "profiles", profileId);

    public string GetConfigPath(string profileId)
    {
        var dir = GetProfileDirectory(profileId);
        var profileFile = Path.Combine(dir, "profile.json");
        if (!File.Exists(profileFile))
            return Path.Combine(dir, "wireguard.conf");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(profileFile));
            if (doc.RootElement.TryGetProperty("configFileName", out var name))
            {
                var fileName = name.GetString();
                if (!string.IsNullOrWhiteSpace(fileName))
                    return Path.Combine(dir, fileName);
            }
        }
        catch
        {
        }

        return Path.Combine(dir, "wireguard.conf");
    }

    public string GetConfigPath(VpnProfile profile) =>
        Path.Combine(GetProfileDirectory(profile.Id), profile.ConfigFileName);

    private async Task<VpnProfile> NormalizeProfileAsync(
        VpnProfile profile,
        string dir,
        CancellationToken cancellationToken)
    {
        var expectedFileName = $"{profile.ConnectionName}.conf";
        if (string.Equals(profile.ConfigFileName, expectedFileName, StringComparison.Ordinal))
            return profile;

        var oldPath = Path.Combine(dir, profile.ConfigFileName);
        var newPath = Path.Combine(dir, expectedFileName);
        if (File.Exists(oldPath))
        {
            if (File.Exists(newPath))
                File.Delete(oldPath);
            else
                File.Move(oldPath, newPath);
        }

        var updated = profile with { ConfigFileName = expectedFileName };
        await SaveProfileAsync(updated, cancellationToken);
        return updated;
    }

    public static string GetDefaultDataRoot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", "wireguard-gui");
    }

    private sealed class ProfileFile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Backend { get; set; } = nameof(BackendKind.Native);
        public string ConnectionName { get; set; } = string.Empty;
        public DateTimeOffset ImportedAt { get; set; }
        public string ConfigFileName { get; set; } = "wireguard.conf";
        public SplitRoutingFile? SplitRouting { get; set; }

        public VpnProfile ToDomain() =>
            new(
                Id,
                Name,
                Enum.TryParse<BackendKind>(Backend, out var backend) ? backend : BackendKind.Native,
                ConnectionName,
                ImportedAt,
                SplitRouting?.ToDomain() ?? SplitRoutingSettings.CreateDefault(),
                ConfigFileName);

        public static ProfileFile FromDomain(VpnProfile profile) =>
            new()
            {
                Id = profile.Id,
                Name = profile.Name,
                Backend = profile.Backend.ToString(),
                ConnectionName = profile.ConnectionName,
                ImportedAt = profile.ImportedAt,
                ConfigFileName = profile.ConfigFileName,
                SplitRouting = SplitRoutingFile.FromDomain(profile.SplitRouting),
            };
    }

    private sealed class SplitRoutingFile
    {
        public bool Enabled { get; set; }
        public bool Youtube { get; set; } = true;
        public bool Telegram { get; set; } = true;
        public List<string>? CustomDomains { get; set; }
        public bool IncludeCloudflare { get; set; } = true;
        public int MaxRoutes { get; set; } = 200;

        public SplitRoutingSettings ToDomain() =>
            new(
                Enabled,
                Youtube,
                Telegram,
                CustomDomains ?? [],
                IncludeCloudflare,
                MaxRoutes);

        public static SplitRoutingFile FromDomain(SplitRoutingSettings settings) =>
            new()
            {
                Enabled = settings.Enabled,
                Youtube = settings.Youtube,
                Telegram = settings.Telegram,
                CustomDomains = settings.CustomDomains.ToList(),
                IncludeCloudflare = settings.IncludeCloudflare,
                MaxRoutes = settings.MaxRoutes,
            };
    }
}
