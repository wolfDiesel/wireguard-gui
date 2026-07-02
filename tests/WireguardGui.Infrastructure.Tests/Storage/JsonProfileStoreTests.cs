using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Infrastructure.Tests.Storage;

public class JsonProfileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-gui-store-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProfileStore(root);
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
}
