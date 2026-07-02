using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Application.Tests.Handlers;

public class GetProfileSplitRoutingHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-split-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProfileStore(root, NullLogger<JsonProfileStore>.Instance);
        var profile = VpnProfile.Create("p", BackendKind.Native, "p") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Twitch = true },
        };

        try
        {
            await store.SaveProfileAsync(profile);
            var handler = new GetProfileSplitRoutingHandler(store);
            var result = await handler.HandleAsync(profile.Id);

            Assert.True(result.Success);
            Assert.True(result.Settings!.Twitch);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class SaveProfileSplitRoutingHandlerTests
{
    [Fact]
    public async Task HandleAsync_NormalizesSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-save-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProfileStore(root, NullLogger<JsonProfileStore>.Instance);
        var profile = VpnProfile.Create("p", BackendKind.Native, "p");

        try
        {
            await store.SaveProfileAsync(profile);
            var handler = new SaveProfileSplitRoutingHandler(store);
            var result = await handler.HandleAsync(
                profile.Id,
                new SplitRoutingSettings(true, true, true, false, [" dup.com ", "dup.com"], false, 0));

            Assert.True(result.Success);
            var loaded = await store.GetProfileAsync(profile.Id);
            Assert.Equal(SplitRoutingSettings.DefaultMaxRoutes, loaded!.SplitRouting.MaxRoutes);
            Assert.Single(loaded.SplitRouting.CustomDomains);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
