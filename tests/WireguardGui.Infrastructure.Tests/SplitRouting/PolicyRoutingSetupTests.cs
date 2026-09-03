using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
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
        Assert.False(CreateContext(new TrackingProcessRunner { IpAvailable = false }).Setup.IsAvailable);
        Assert.True(CreateContext(new TrackingProcessRunner()).Setup.IsAvailable);
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
    public async Task SyncRoutesAsync_RefreshesBaselineWhenRoutesUnchanged()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var routes = new[] { "1.1.1.1/32" };
        var table = PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString();

        await context.Setup.ApplyAsync(context.Profile, routes);
        runner.PrivilegedCommands.Clear();

        var sync = await context.Setup.SyncRoutesAsync(context.Profile, routes);

        Assert.False(sync.RoutesChanged);
        Assert.Contains(
            runner.PrivilegedCommands,
            c => MatchesIp(c, "route", "replace", "default", "dev", "wg0", "table", table));
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["dns", "wg0", "8.8.8.8"] });
    }

    [Fact]
    public async Task SyncRoutesAsync_ForceReinstallsRulesWhenUnchanged()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var routes = new[] { "1.1.1.1/32" };
        var table = PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString();

        await context.Setup.ApplyAsync(context.Profile, routes);
        runner.PrivilegedCommands.Clear();

        var sync = await context.Setup.SyncRoutesAsync(context.Profile, routes, force: true);

        Assert.True(sync.RoutesChanged);
        Assert.Contains(
            runner.PrivilegedCommands,
            c => MatchesIp(c, "rule", "add", "pref", "100", "to", "1.1.1.1/32", "lookup", table));
    }

    [Fact]
    public async Task ApplyAsync_ClearsOrphanManagedTables()
    {
        var keep = PolicyRoutingNaming.RoutingTableId("fixed-profile-id");
        var orphan = keep == 150 ? 151 : 150;
        var runner = new TrackingProcessRunner
        {
            RuleListOutput = $"100:\tfrom all to 9.9.9.9 lookup {orphan}\n",
        };
        var context = CreateContext(runner, profileId: "fixed-profile-id");

        var result = await context.Setup.ApplyAsync(context.Profile, ["1.1.1.1/32"]);

        Assert.True(result.Success);
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "flush", "table", orphan.ToString()));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "route", "flush", "table", orphan.ToString()));
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
        Assert.True(context.DnsProxy.Stopped);
    }

    [Fact]
    public async Task ApplyAsync_ConfiguresTunnelDns()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);

        var result = await context.Setup.ApplyAsync(context.Profile, ["1.1.1.1/32"]);

        Assert.True(result.Success);
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "nmcli", Arguments: var a } &&
                 a.Contains("ipv4.dns") &&
                 a.Contains("8.8.8.8"));
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["dns", "wg0", "8.8.8.8"] });
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["domain", "wg0", "~."] });
    }

    [Fact]
    public async Task ApplyAsync_PublicDns_CapturesAllDomains()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner, dns: "8.8.8.8");

        var result = await context.Setup.ApplyAsync(context.Profile, ["1.1.1.1/32"]);

        Assert.True(result.Success);
        Assert.Contains(
            runner.PrivilegedCommands,
            c => c is { FileName: "resolvectl", Arguments: ["dns", "wg0", "8.8.8.8"] });
        Assert.Contains(
            runner.PrivilegedCommands,
            c => MatchesIp(c, "rule", "add", "pref", "100", "to", "8.8.8.8/32", "lookup",
                PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString()));
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

    private static TestContext CreateContext(
        TrackingProcessRunner runner,
        string? dns = null,
        string? profileId = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        if (profileId is not null)
            profile = profile with { Id = profileId };
        Directory.CreateDirectory(store.GetProfileDirectory(profile.Id));
        var dnsLine = dns is null ? string.Empty : $"DNS = {dns}\n";
        File.WriteAllText(
            store.GetConfigPath(profile),
            $"""
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Address = 10.8.0.5/32
            {dnsLine}[Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = vpn.example.com:51820
            AllowedIPs = 0.0.0.0/0
            """);

        var dnsProxy = new FakeDomainRouteDnsProxy();
        var setup = new PolicyRoutingSetup(
            runner,
            store,
            new WireGuardConfigParser(),
            dnsProxy,
            NullLogger<PolicyRoutingSetup>.Instance);

        return new TestContext(setup, profile, root, dnsProxy);
    }

    private sealed record TestContext(
        PolicyRoutingSetup Setup,
        VpnProfile Profile,
        string Root,
        FakeDomainRouteDnsProxy DnsProxy);

    private sealed class FakeDomainRouteDnsProxy : IDomainRouteDnsProxy
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool IsRunning => Started && !Stopped;

        public Task StartAsync(
            string profileId,
            IReadOnlyList<string> suffixes,
            IReadOnlyList<string> upstreamDns,
            Func<IReadOnlyList<string>, CancellationToken, Task> onResolvedHosts,
            CancellationToken cancellationToken = default)
        {
            Started = true;
            Stopped = false;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Stopped = true;
            Started = false;
            return Task.CompletedTask;
        }
    }
}
