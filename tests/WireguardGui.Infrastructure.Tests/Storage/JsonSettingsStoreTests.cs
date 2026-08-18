using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Infrastructure.Tests.Storage;

public class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsRefreshMinutes()
    {
        var path = Path.Combine(Path.GetTempPath(), "wg-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            await store.SaveAsync(new AppSettings(UiSettings.CreateDefault(), 7));
            var loaded = await store.LoadAsync();
            Assert.Equal(7, loaded.SplitRoutingRefreshMinutes);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_MissingRefreshMinutes_DefaultsToTen()
    {
        var path = Path.Combine(Path.GetTempPath(), "wg-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{ "ui": { "language": "en" } }""");
            var store = new JsonSettingsStore(path);
            var loaded = await store.LoadAsync();
            Assert.Equal(AppSettings.DefaultRefreshMinutes, loaded.SplitRoutingRefreshMinutes);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Save_ClampsRefreshMinutes()
    {
        var path = Path.Combine(Path.GetTempPath(), "wg-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            await store.SaveAsync(new AppSettings(UiSettings.CreateDefault(), 0));
            var loaded = await store.LoadAsync();
            Assert.Equal(AppSettings.DefaultRefreshMinutes, loaded.SplitRoutingRefreshMinutes);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
