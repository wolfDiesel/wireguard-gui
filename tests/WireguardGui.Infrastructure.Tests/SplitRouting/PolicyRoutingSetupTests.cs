using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class PolicyRoutingSetupTests
{
    [Fact]
    public void IsAvailable_RequiresIp()
    {
        var unavailable = CreateContext(new TrackingProcessRunner { IpAvailable = false }).Setup;
        Assert.False(unavailable.IsAvailable);

        var available = CreateContext(new TrackingProcessRunner()).Setup;
        Assert.True(available.IsAvailable);
    }

    [Fact]
    public async Task ApplyAsync_AddsDestinationIpRules()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var table = PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString();

        var result = await context.Setup.ApplyAsync(
            context.Profile,
            ["1.1.1.1/32", "149.154.160.0/20", "2001:db8::1/128"]);

        Assert.True(result.Success);
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "add", "pref", "100", "to", "149.154.160.0/20", "lookup", table));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "-6", "rule", "add", "pref", "100", "to", "2001:db8::1/128", "lookup", table));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "route", "replace", "default", "dev", "wg0", "table", table));
    }

    [Fact]
    public async Task SyncRoutesAsync_SkipsWhenRoutesUnchanged()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var routes = new[] { "1.1.1.1/32" };

        await context.Setup.ApplyAsync(context.Profile, routes);
        runner.PrivilegedCommands.Clear();

        var sync = await context.Setup.SyncRoutesAsync(context.Profile, routes);

        Assert.False(sync.RoutesChanged);
        Assert.Empty(runner.PrivilegedCommands);
    }

    [Fact]
    public async Task ApplyAsync_SkipsIpv6WhenInterfaceHasNoIpv6()
    {
        var runner = new TrackingProcessRunner { InterfaceIpv6Capable = false };
        var context = CreateContext(runner);
        var table = PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString();

        var result = await context.Setup.ApplyAsync(
            context.Profile,
            ["1.1.1.1/32", "2001:db8::1/128"]);

        Assert.True(result.Success);
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "add", "pref", "100", "to", "1.1.1.1/32", "lookup", table));
        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments is ["-6", "rule", "add", ..]);
    }

    [Fact]
    public async Task TeardownAsync_RemovesPolicyArtifacts()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var table = PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString();

        await context.Setup.TeardownAsync(context.Profile);

        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "flush", "table", table));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "route", "flush", "table", table));
    }

    [Fact]
    public async Task TeardownAsync_IgnoresMissingFibTable()
    {
        var runner = new TrackingProcessRunner { FailRouteFlush = true };
        var context = CreateContext(runner);

        var exception = await Record.ExceptionAsync(() => context.Setup.TeardownAsync(context.Profile));

        Assert.Null(exception);
    }

    private static bool MatchesIp((string FileName, string[] Arguments) command, params string[] expected) =>
        command.FileName == "ip" && command.Arguments.SequenceEqual(expected);

    private static TestContext CreateContext(TrackingProcessRunner runner)
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("p1", BackendKind.Native, "wg0");
        Directory.CreateDirectory(store.GetProfileDirectory(profile.Id));
        File.WriteAllText(
            store.GetConfigPath(profile),
            """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = vpn.example.com:51820
            AllowedIPs = 0.0.0.0/0
            """);

        var setup = new PolicyRoutingSetup(
            runner,
            store,
            new WireGuardConfigParser(),
            NullLogger<PolicyRoutingSetup>.Instance);

        return new TestContext(setup, profile, root);
    }

    private sealed record TestContext(PolicyRoutingSetup Setup, VpnProfile Profile, string Root);
}
