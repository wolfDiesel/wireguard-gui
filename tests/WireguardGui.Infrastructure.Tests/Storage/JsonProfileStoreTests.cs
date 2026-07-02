using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.Tests.Fakes;

namespace WireguardGui.Infrastructure.Tests.Storage;

public class JsonProfileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-gui-store-" + Guid.NewGuid().ToString("N"));
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("test", BackendKind.Native, "test") with
        {
            SplitRouting = new SplitRoutingSettings(true, true, false, true, ["a.com"], true, 100),
        };

        try
        {
            await store.SaveProfileAsync(profile);
            var loaded = await store.GetProfileAsync(profile.Id);
            Assert.NotNull(loaded);
            Assert.Equal("test", loaded.Name);
            Assert.True(loaded.SplitRouting.Enabled);
            Assert.True(loaded.SplitRouting.Twitch);
            Assert.Equal("a.com", loaded.SplitRouting.CustomDomains[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetProfileAsync_DoesNotRewriteProfileFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-gui-store-" + Guid.NewGuid().ToString("N"));
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("test", BackendKind.Native, "test");

        try
        {
            await store.SaveProfileAsync(profile);
            var profileFile = Path.Combine(store.GetProfileDirectory(profile.Id), "profile.json");
            var before = File.GetLastWriteTimeUtc(profileFile);

            await Task.Delay(50);
            await store.GetProfileAsync(profile.Id);
            var after = File.GetLastWriteTimeUtc(profileFile);

            Assert.Equal(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyJson_WithoutIncludeCloudflare_DefaultsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-gui-store-" + Guid.NewGuid().ToString("N"));
        var store = TestStoreFactory.Create(root);
        var id = Guid.NewGuid().ToString("N");
        var dir = store.GetProfileDirectory(id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "profile.json"), """
            {
              "id": "LEGACY",
              "name": "legacy",
              "backend": "Native",
              "connectionName": "legacy",
              "importedAt": "2026-01-01T00:00:00Z",
              "configFileName": "legacy.conf",
              "splitRouting": {
                "enabled": true,
                "youtube": true,
                "telegram": true,
                "twitch": false,
                "customDomains": []
              }
            }
            """.Replace("LEGACY", id));

        try
        {
            var loaded = await store.GetProfileAsync(id);
            Assert.NotNull(loaded);
            Assert.False(loaded.SplitRouting.IncludeCloudflare);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
