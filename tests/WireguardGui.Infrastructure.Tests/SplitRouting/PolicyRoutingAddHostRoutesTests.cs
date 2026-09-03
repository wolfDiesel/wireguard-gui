using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class PolicyRoutingAddHostRoutesTests
{
    [Fact]
    public async Task AddHostRoutesAsync_BatchesThroughPrivilegedShell()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);
        var table = PolicyRoutingNaming.RoutingTableId(context.Profile.Id).ToString();

        await context.Setup.ApplyAsync(context.Profile, ["1.1.1.1/32"]);
        runner.PrivilegedShellScripts.Clear();
        runner.PrivilegedCommands.Clear();

        await context.Setup.AddHostRoutesAsync(
            context.Profile,
            ["8.8.4.4/32", "8.8.4.4/32", "9.9.9.9"]);

        Assert.Contains(
            runner.PrivilegedShellScripts,
            script => script.Contains($"to '8.8.4.4/32' lookup {table}", StringComparison.Ordinal) &&
                      script.Contains($"to '9.9.9.9/32' lookup {table}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            runner.PrivilegedCommands,
            c => c.FileName == "ip" && c.Arguments is ["rule", "add", ..]);
    }

    [Fact]
    public async Task AddHostRoutesAsync_SkipsAlreadyInstalled()
    {
        var runner = new TrackingProcessRunner();
        var context = CreateContext(runner);

        await context.Setup.ApplyAsync(context.Profile, ["1.1.1.1/32"]);
        runner.PrivilegedShellScripts.Clear();

        await context.Setup.AddHostRoutesAsync(context.Profile, ["1.1.1.1/32"]);

        Assert.Empty(runner.PrivilegedShellScripts);
    }

    private static TestContext CreateContext(TrackingProcessRunner runner)
    {
        var root = Path.Combine(Path.GetTempPath(), "wg-addhost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = TestStoreFactory.Create(root);
        var profile = VpnProfile.Create("p1", BackendKind.Nmcli, "wg0") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with
            {
                Enabled = true,
                Twitch = true,
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

        var setup = new PolicyRoutingSetup(
            runner,
            store,
            new WireGuardConfigParser(),
            new FakeDomainRouteDnsProxy(),
            NullLogger<PolicyRoutingSetup>.Instance);

        return new TestContext(setup, profile);
    }

    private sealed record TestContext(PolicyRoutingSetup Setup, VpnProfile Profile);

    private sealed class FakeDomainRouteDnsProxy : IDomainRouteDnsProxy
    {
        public bool IsRunning => false;

        public Task StartAsync(
            string profileId,
            IReadOnlyList<string> suffixes,
            IReadOnlyList<string> upstreamDns,
            Func<IReadOnlyList<string>, CancellationToken, Task> onResolvedHosts,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
