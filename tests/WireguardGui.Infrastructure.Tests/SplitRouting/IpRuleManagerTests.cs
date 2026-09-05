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

    private static bool MatchesIp((string FileName, string[] Arguments) command, params string[] expected) =>
        command.FileName == "ip" && command.Arguments.SequenceEqual(expected);
}