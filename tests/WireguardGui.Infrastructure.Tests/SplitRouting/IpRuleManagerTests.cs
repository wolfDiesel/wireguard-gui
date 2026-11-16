using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class IpRuleManagerTests
{
    [Fact]
    public async Task SyncDestinationRulesAsync_ReplaceAll_InstallsRules()
    {
        var runner = new TrackingProcessRunner();
        var manager = new IpRuleManager(runner, NullLogger<IpRuleManager>.Instance);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id).ToString();

        await manager.SyncDestinationRulesAsync(
            profile,
            "wg0",
            ["1.1.1.1/32", "149.154.160.0/20"],
            replaceAll: true,
            readdAll: false,
            new ConcurrentDictionary<string, HashSet<string>>(),
            CancellationToken.None);

        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "add", "pref", "100", "to", "1.1.1.1/32", "lookup", table));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "add", "pref", "100", "to", "149.154.160.0/20", "lookup", table));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "route", "replace", "default", "dev", "wg0", "table", table));
    }

    [Fact]
    public async Task SyncDestinationRulesAsync_Merge_AddsNewAndRemovesOld()
    {
        var runner = new TrackingProcessRunner();
        var manager = new IpRuleManager(runner, NullLogger<IpRuleManager>.Instance);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id).ToString();
        var bulk = new ConcurrentDictionary<string, HashSet<string>>();
        bulk[profile.Id] = ["149.154.160.0/20"];

        await manager.SyncDestinationRulesAsync(
            profile,
            "wg0",
            ["2.2.2.2/32"],
            replaceAll: false,
            readdAll: false,
            bulk,
            CancellationToken.None);

        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "add", "pref", "100", "to", "2.2.2.2/32", "lookup", table));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "del", "pref", "100", "to", "149.154.160.0/20", "lookup", table));
    }

    [Fact]
    public async Task CleanupOrphanPolicyTablesAsync_FlushesOrphanTables()
    {
        var keep = PolicyRoutingNaming.RoutingTableId("fixed-profile-id");
        var orphan = keep == 150 ? 151 : 150;
        var runner = new TrackingProcessRunner
        {
            RuleListOutput = $"100:\tfrom all to 9.9.9.9 lookup {orphan}\n",
        };
        var manager = new IpRuleManager(runner, NullLogger<IpRuleManager>.Instance);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0") with { Id = "fixed-profile-id" };

        await manager.CleanupOrphanPolicyTablesAsync(profile, CancellationToken.None);

        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "rule", "flush", "table", orphan.ToString()));
        Assert.Contains(runner.PrivilegedCommands, c => MatchesIp(c, "route", "flush", "table", orphan.ToString()));
    }

    [Fact]
    public async Task PruneDestinationRulesAsync_RemovesRulesThatAreNoLongerTracked()
    {
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var runner = new TrackingProcessRunner
        {
            RuleListOutput =
                $"100:\tfrom all to 45.129.128.77 lookup {table}\n" +
                $"100:\tfrom all to 1.1.1.1 lookup {table}\n" +
                $"100:\tfrom all to 8.8.8.8 lookup {table}\n" +
                "100:\tfrom all to 9.9.9.9 lookup 250\n" +
                "32766:\tfrom all lookup main\n",
            Ipv6RuleListOutput = $"100:\tfrom all to 2001:db8::1 lookup {table}\n",
        };
        var manager = new IpRuleManager(runner, NullLogger<IpRuleManager>.Instance);

        var pruned = await manager.PruneDestinationRulesAsync(
            profile,
            ["1.1.1.1/32", "8.8.8.8/32"],
            CancellationToken.None);

        Assert.Equal(2, pruned);
        Assert.Contains(
            runner.PrivilegedCommands,
            c => MatchesIp(c, "rule", "del", "pref", "100", "to", "45.129.128.77", "lookup", table.ToString()));
        Assert.Contains(
            runner.PrivilegedCommands,
            c => MatchesIp(c, "-6", "rule", "del", "pref", "100", "to", "2001:db8::1", "lookup", table.ToString()));
        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments.Contains("1.1.1.1"));
        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments.Contains("8.8.8.8"));
        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments.Contains("9.9.9.9"));
    }

    [Fact]
    public async Task PruneDestinationRulesAsync_NothingStale_DoesNotTouchRules()
    {
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var runner = new TrackingProcessRunner
        {
            RuleListOutput = $"100:\tfrom all to 1.1.1.1 lookup {table}\n",
        };
        var manager = new IpRuleManager(runner, NullLogger<IpRuleManager>.Instance);

        var pruned = await manager.PruneDestinationRulesAsync(profile, ["1.1.1.1/32"], CancellationToken.None);

        Assert.Equal(0, pruned);
        Assert.DoesNotContain(runner.PrivilegedCommands, c => c.FileName == "ip");
    }

    [Fact]
    public async Task PruneDestinationRulesAsync_Ipv6HostAndPrefix_AreMatchedByKeepSet()
    {
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0");
        var table = PolicyRoutingNaming.RoutingTableId(profile.Id);
        var runner = new TrackingProcessRunner
        {
            Ipv6RuleListOutput =
                $"100:\tfrom all to 2001:db8::1 lookup {table}\n" +
                $"100:\tfrom all to 2001:db8::/32 lookup {table}\n",
        };
        var manager = new IpRuleManager(runner, NullLogger<IpRuleManager>.Instance);

        var pruned = await manager.PruneDestinationRulesAsync(
            profile,
            ["2001:db8::1/128", "2001:db8::/32"],
            CancellationToken.None);

        Assert.Equal(0, pruned);
        Assert.DoesNotContain(runner.PrivilegedCommands, c => c.FileName == "ip");
    }

    [Fact]
    public void TryParseManagedRule_ParsesOnlyOwnTableAndPreference()
    {
        Assert.True(IpRuleManager.TryParseManagedRule("100:\tfrom all to 45.129.128.77 lookup 232", 232, out var cidr));
        Assert.Equal("45.129.128.77", cidr);
        Assert.False(IpRuleManager.TryParseManagedRule("100:\tfrom all to 45.129.128.77 lookup 232", 233, out _));
        Assert.False(IpRuleManager.TryParseManagedRule("32765:\tfrom all fwmark 0x1 lookup 232", 232, out _));
        Assert.False(IpRuleManager.TryParseManagedRule("100:\tfrom all to all lookup 232", 232, out _));
        Assert.False(IpRuleManager.TryParseManagedRule("32766:\tfrom all lookup main", 232, out _));
    }

    private static bool MatchesIp((string FileName, string[] Arguments) command, params string[] expected) =>
        command.FileName == "ip" && command.Arguments.SequenceEqual(expected);
}