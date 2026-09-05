using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.WireGuard;

public class WireGuardConfigRepositoryTests
{
    private const string SampleConfig = """
        [Interface]
        PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Address = 10.8.0.5/32
        [Peer]
        PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        AllowedIPs = 0.0.0.0/0
        """;

    private static (JsonProfileStore Store, VpnProfile Profile, string Root) CreateContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-repo-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProfileStore(root, NullLogger<JsonProfileStore>.Instance);
        var profile = VpnProfile.Create("p", BackendKind.Native, "p");
        return (store, profile, root);
    }

    [Fact]
    public async Task ReadAsync_ReturnsCurrentContent()
    {
        var (store, profile, root) = CreateContext();
        try
        {
            await store.SaveProfileAsync(profile);
            await File.WriteAllTextAsync(store.GetConfigPath(profile), SampleConfig);
            var repo = new WireGuardConfigRepository(store, new WireGuardConfigParser());

            var content = await repo.ReadAsync(profile);

            Assert.Equal(SampleConfig, content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenConfigMissing()
    {
        var (store, profile, root) = CreateContext();
        try
        {
            await store.SaveProfileAsync(profile);
            var repo = new WireGuardConfigRepository(store, new WireGuardConfigParser());

            await Assert.ThrowsAsync<FileNotFoundException>(() => repo.ReadAsync(profile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_OverwritesContent()
    {
        var (store, profile, root) = CreateContext();
        try
        {
            await store.SaveProfileAsync(profile);
            await File.WriteAllTextAsync(store.GetConfigPath(profile), SampleConfig);
            var repo = new WireGuardConfigRepository(store, new WireGuardConfigParser());

            var updated = SampleConfig.Replace("AllowedIPs = 0.0.0.0/0", "AllowedIPs = 10.0.0.0/8");
            await repo.WriteAsync(profile, updated);

            Assert.Equal(updated, await File.ReadAllTextAsync(store.GetConfigPath(profile)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateAsync_AppliesTransformAndWrites()
    {
        var (store, profile, root) = CreateContext();
        try
        {
            await store.SaveProfileAsync(profile);
            await File.WriteAllTextAsync(store.GetConfigPath(profile), SampleConfig);
            var repo = new WireGuardConfigRepository(store, new WireGuardConfigParser());

            var result = await repo.UpdateAsync(profile, c => c.Replace("0.0.0.0/0", "10.0.0.0/8"));

            Assert.Contains("AllowedIPs = 10.0.0.0/8", result);
            Assert.Contains("AllowedIPs = 10.0.0.0/8", await File.ReadAllTextAsync(store.GetConfigPath(profile)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateAsync_DoesNotWriteWhenUnchanged()
    {
        var (store, profile, root) = CreateContext();
        try
        {
            await store.SaveProfileAsync(profile);
            await File.WriteAllTextAsync(store.GetConfigPath(profile), SampleConfig);
            var repo = new WireGuardConfigRepository(store, new WireGuardConfigParser());

            var result = await repo.UpdateAsync(profile, c => c);

            Assert.Equal(SampleConfig, result);
            Assert.Equal(SampleConfig, await File.ReadAllTextAsync(store.GetConfigPath(profile)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}