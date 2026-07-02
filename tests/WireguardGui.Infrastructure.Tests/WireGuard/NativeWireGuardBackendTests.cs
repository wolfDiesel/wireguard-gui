using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.SplitRouting.Sources;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.Tests.Fakes;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.WireGuard;

public class NativeWireGuardBackendTests
{
    [Fact]
    public async Task GetConnectionState_ParsesInterfaceColumn_ExactMatch()
    {
        var runner = new FakeProcessRunner
        {
            WgShowOutput = """
                interface: wg0
                  public key: abc
                interface: wg-office
                  public key: def
                """,
        };
        var store = TestStoreFactory.Create(Path.Combine(Path.GetTempPath(), "wg-" + Guid.NewGuid().ToString("N")));
        var backend = new NativeWireGuardBackend(runner, store);
        var profile = VpnProfile.Create("p", BackendKind.Native, "wg-office");

        var state = await backend.GetConnectionStateAsync(profile);

        Assert.Equal(ConnectionState.Connected, state);
    }

    [Fact]
    public async Task GetConnectionState_SubstringTrap_DoesNotMatch()
    {
        var runner = new FakeProcessRunner
        {
            WgShowOutput = """
                interface: wg0
                  public key: abc
                """,
        };
        var store = TestStoreFactory.Create(Path.Combine(Path.GetTempPath(), "wg-" + Guid.NewGuid().ToString("N")));
        var backend = new NativeWireGuardBackend(runner, store);
        var profile = VpnProfile.Create("p", BackendKind.Native, "wg");

        var state = await backend.GetConnectionStateAsync(profile);

        Assert.Equal(ConnectionState.Disconnected, state);
    }
}

public class WireGuardConfigParserEdgeTests
{
    private readonly WireGuardConfigParser _parser = new();

    [Fact]
    public void WriteAllowedIps_ReplacesAllAllowedIpsLines_KnownLimitation()
    {
        const string config = """
            [Interface]
            PrivateKey = abc=
            [Peer]
            PublicKey = def=
            AllowedIPs = 10.0.0.0/8
            [Peer]
            PublicKey = ghi=
            AllowedIPs = 192.168.0.0/16
            """;

        var updated = _parser.WriteAllowedIps(config, "1.1.1.1/32");
        Assert.DoesNotContain("10.0.0.0/8", updated);
        Assert.DoesNotContain("192.168.0.0/16", updated);
        Assert.Equal(2, updated.Split("AllowedIPs = 1.1.1.1/32", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void HasDns_DetectsCommentedDnsLine()
    {
        const string config = """
            [Interface]
            PrivateKey = abc=
            # DNS = 1.1.1.1
            [Peer]
            PublicKey = def=
            """;
        Assert.False(_parser.HasDns(config));
    }

    [Fact]
    public void NormalizeAllowedIps_SortsAndTrims()
    {
        var normalized = _parser.NormalizeAllowedIps(" 2.2.2.2/32,1.1.1.1/32 ");
        Assert.Equal("1.1.1.1/32,2.2.2.2/32", normalized);
    }
}

public class SplitRoutingConfigUpdaterTests
{
    [Fact]
    public async Task TryUpdateConfig_UnchangedAllowedIPs_ReturnsChangedFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-updater-" + Guid.NewGuid().ToString("N"));
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("test", BackendKind.Native, "test") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Enabled = true, Youtube = false, Telegram = true },
        };

        Directory.CreateDirectory(store.GetProfileDirectory(profile.Id));
        await File.WriteAllTextAsync(store.GetConfigPath(profile), """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            AllowedIPs = 149.154.160.0/20,91.108.4.0/22,91.108.8.0/22,91.108.16.0/22,91.108.56.0/22,91.105.192.0/23,95.161.64.0/20,185.76.151.0/24
            """);

        try
        {
            var builder = new SplitRouteBuilder(
                [new TelegramSplitRouteSource()],
                NullLogger<SplitRouteBuilder>.Instance);
            var updater = new SplitRoutingConfigUpdater(
                store,
                builder,
                new WireGuardConfigParser(),
                NullLogger<SplitRoutingConfigUpdater>.Instance);

            var result = await updater.TryUpdateConfigAsync(profile);

            Assert.False(result.Changed);
            Assert.Null(result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
