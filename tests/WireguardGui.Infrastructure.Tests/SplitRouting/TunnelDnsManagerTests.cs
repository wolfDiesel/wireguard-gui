using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class TunnelDnsManagerTests
{
    [Fact]
    public async Task EnsureTunnelDnsAsync_ConfiguresResolvectl()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);

        await context.Manager.EnsureTunnelDnsAsync(
            context.Profile,
            "wg0",
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(),
            CancellationToken.None);

        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["dns", "wg0", "8.8.8.8"] });
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: var a } &&
                 a.Length > 3 &&
                 a[0] == "domain" &&
                 a[1] == "wg0" &&
                 a.Contains("~example.com") &&
                 a.Contains("~ttvnw.net") &&
                 a.Contains("~telegram.org") &&
                 !a.Contains("~."));
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["default-route", "wg0", "no"] });
    }

    [Fact]
    public async Task EnsureTunnelDnsAsync_SplitRoutingDisabled_ClearsRoutingDomains()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var profile = context.Profile with
        {
            SplitRouting = context.Profile.SplitRouting with { Enabled = false },
        };

        await context.Manager.EnsureTunnelDnsAsync(
            profile,
            "wg0",
            new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(),
            CancellationToken.None);

        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["domain", "wg0"] });
        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: var a } && a.Contains("~."));
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["default-route", "wg0", "no"] });
    }

    [Fact]
    public async Task ClearTunnelDnsAsync_RevertsInterface()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);

        await context.Manager.ClearTunnelDnsAsync("wg0", CancellationToken.None);

        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["revert", "wg0"] });
    }

    private static TestContext CreateContext(TrackingProcessRunner runner)
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-dns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with
            {
                Enabled = true,
                Twitch = true,
                CustomDomains = ["example.com"],
            },
        };
        Directory.CreateDirectory(store.GetProfileDirectory(profile.Id));
        File.WriteAllText(
            store.GetConfigPath(profile),
            """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Address = 10.8.0.5/32
            DNS = 8.8.8.8
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = vpn.example.com:51820
            AllowedIPs = 0.0.0.0/0
            """);

        var manager = new TunnelDnsManager(
            runner,
            store,
            new WireGuardConfigParser(),
            NullLogger<TunnelDnsManager>.Instance);

        return new TestContext(manager, profile);
    }

    private sealed record TestContext(TunnelDnsManager Manager, VpnProfile Profile);
}