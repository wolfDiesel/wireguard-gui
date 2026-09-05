using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class EndpointRouteGuardTests
{
    [Fact]
    public async Task EnsureEndpointRouteAsync_UsesNonWireGuardDefaultRoute()
    {
        var runner = new TrackingProcessRunner
        {
            DefaultRoute = "default via 192.168.1.1 dev enp3s0",
            WgInterfaces = "wg0",
        };
        var context = CreateContext(runner);

        await context.Guard.EnsureEndpointRouteAsync(context.Profile, CancellationToken.None);

        Assert.Contains(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments.SequenceEqual(
                ["route", "replace", "1.2.3.4/32", "via", "192.168.1.1", "dev", "enp3s0"]));
    }

    [Fact]
    public async Task EnsureEndpointRouteAsync_SkipsWireGuardDefaultRoute()
    {
        var runner = new TrackingProcessRunner
        {
            DefaultRoute = "default via 192.168.1.1 dev wg0",
            WgInterfaces = "wg0",
        };
        var context = CreateContext(runner);

        await context.Guard.EnsureEndpointRouteAsync(context.Profile, CancellationToken.None);

        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments.Length >= 2 &&
                 c.Arguments[0] == "route" && c.Arguments[1] == "replace" &&
                 c.Arguments.Contains("1.2.3.4/32"));
    }

    private static TestContext CreateContext(TrackingProcessRunner runner)
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-endpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        Directory.CreateDirectory(store.GetProfileDirectory(profile.Id));
        File.WriteAllText(
            store.GetConfigPath(profile),
            """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Address = 10.8.0.5/32
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = vpn.example.com:51820
            AllowedIPs = 0.0.0.0/0
            """);

        var guard = new EndpointRouteGuard(
            runner,
            store,
            new WireGuardConfigParser(),
            NullLogger<EndpointRouteGuard>.Instance);

        return new TestContext(guard, profile);
    }

    private sealed record TestContext(EndpointRouteGuard Guard, VpnProfile Profile);
}