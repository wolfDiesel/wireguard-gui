using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.WireGuard;

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
            Assert.Equal(TunnelDnsServers.Default, result.Settings.TunnelDns);
            Assert.Equal(TunnelDnsServers.Default, result.TunnelDnsPlaceholder);
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
        var parser = new WireGuardConfigParser();
        var dnsSync = new ProfileConfigDnsSync(new WireGuardConfigRepository(store, parser), parser);
        var profile = VpnProfile.Create("p", BackendKind.Native, "p");

        try
        {
            await store.SaveProfileAsync(profile);
            await File.WriteAllTextAsync(
                store.GetConfigPath(profile.Id),
                """
                [Interface]
                PrivateKey = x
                Address = 10.8.0.5/32
                [Peer]
                PublicKey = y
                AllowedIPs = 0.0.0.0/0
                """);
            var handler = new SaveProfileSplitRoutingHandler(store, dnsSync);
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

    [Fact]
    public async Task HandleAsync_WritesTunnelDnsToConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-save-dns-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProfileStore(root, NullLogger<JsonProfileStore>.Instance);
        var parser = new WireGuardConfigParser();
        var dnsSync = new ProfileConfigDnsSync(new WireGuardConfigRepository(store, parser), parser);
        var profile = VpnProfile.Create("p", BackendKind.Native, "p");

        try
        {
            await store.SaveProfileAsync(profile);
            var configPath = store.GetConfigPath(profile.Id);
            await File.WriteAllTextAsync(
                configPath,
                """
                [Interface]
                PrivateKey = x
                Address = 10.8.0.5/32
                [Peer]
                PublicKey = y
                AllowedIPs = 0.0.0.0/0
                """);

            var handler = new SaveProfileSplitRoutingHandler(store, dnsSync);
            var settings = SplitRoutingSettings.CreateDefault() with { TunnelDns = "10.8.0.1" };
            var result = await handler.HandleAsync(profile.Id, settings);

            Assert.True(result.Success);
            var config = await File.ReadAllTextAsync(configPath);
            Assert.Contains("DNS = 10.8.0.1", config);
            var loaded = await store.GetProfileAsync(profile.Id);
            Assert.Equal("10.8.0.1", loaded!.SplitRouting.TunnelDns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_RejectsInvalidTunnelDns()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-save-bad-dns-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProfileStore(root, NullLogger<JsonProfileStore>.Instance);
        var parser = new WireGuardConfigParser();
        var dnsSync = new ProfileConfigDnsSync(new WireGuardConfigRepository(store, parser), parser);
        var profile = VpnProfile.Create("p", BackendKind.Native, "p");

        try
        {
            await store.SaveProfileAsync(profile);
            var handler = new SaveProfileSplitRoutingHandler(store, dnsSync);
            var result = await handler.HandleAsync(
                profile.Id,
                SplitRoutingSettings.CreateDefault() with { TunnelDns = "bad" });

            Assert.False(result.Success);
            Assert.Equal(OperationErrorCode.ConfigInvalid, result.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
