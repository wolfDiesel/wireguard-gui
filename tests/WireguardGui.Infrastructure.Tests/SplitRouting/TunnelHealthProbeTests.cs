using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class TunnelHealthProbeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void Evaluate_FreshHandshake_IsHealthy()
    {
        var output = $"key-a\t{Now.ToUnixTimeSeconds() - 30}\nkey-b\t{Now.ToUnixTimeSeconds() - 600}\n";

        var health = TunnelHealthProbe.Evaluate(output, Now);

        Assert.Equal(TunnelHealthState.Healthy, health.State);
        Assert.True(health.AllowsSplitDns);
    }

    [Fact]
    public void Evaluate_OldHandshake_IsStale()
    {
        var output = $"key-a\t{Now.ToUnixTimeSeconds() - (long)TunnelHealthProbe.MaxHandshakeAge.TotalSeconds - 60}\n";

        var health = TunnelHealthProbe.Evaluate(output, Now);

        Assert.Equal(TunnelHealthState.Stale, health.State);
        Assert.False(health.AllowsSplitDns);
    }

    [Fact]
    public void Evaluate_ZeroHandshake_ReportsNoHandshake()
    {
        var health = TunnelHealthProbe.Evaluate("key-a\t0\n", Now);

        Assert.Equal(TunnelHealthState.NoHandshake, health.State);
        Assert.False(health.AllowsSplitDns);
    }

    [Fact]
    public void Evaluate_WithoutPeers_IsUnknownAndDoesNotBlockSplitDns()
    {
        var health = TunnelHealthProbe.Evaluate(string.Empty, Now);

        Assert.Equal(TunnelHealthState.Unknown, health.State);
        Assert.True(health.AllowsSplitDns);
    }

    [Fact]
    public async Task CheckAsync_MissingInterface_ReportsInterfaceMissing()
    {
        var runner = new TrackingProcessRunner { InterfaceLinkPresent = false };
        var probe = new TunnelHealthProbe(runner, NullLogger<TunnelHealthProbe>.Instance);

        var health = await probe.CheckAsync(Profile(), "wg0", CancellationToken.None);

        Assert.Equal(TunnelHealthState.InterfaceMissing, health.State);
        Assert.False(health.AllowsSplitDns);
    }

    [Fact]
    public async Task CheckAsync_UnreadableWg_IsUnknown()
    {
        var runner = new TrackingProcessRunner
        {
            WgLatestHandshakes = "key-a\t0\n",
            WgLatestHandshakesExitCode = 1,
        };
        var probe = new TunnelHealthProbe(runner, NullLogger<TunnelHealthProbe>.Instance);

        var health = await probe.CheckAsync(Profile(), "wg0", CancellationToken.None);

        Assert.Equal(TunnelHealthState.Unknown, health.State);
    }

    [Fact]
    public async Task CheckAsync_StaleHandshake_IsNotUsable()
    {
        var runner = new TrackingProcessRunner
        {
            WgLatestHandshakes = $"key-a\t{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600}\n",
        };
        var probe = new TunnelHealthProbe(runner, NullLogger<TunnelHealthProbe>.Instance);

        var health = await probe.CheckAsync(Profile(), "wg0", CancellationToken.None);

        Assert.Equal(TunnelHealthState.Stale, health.State);
        Assert.False(health.AllowsSplitDns);
    }

    [Fact]
    public async Task CheckAsync_FreshHandshake_IsUsable()
    {
        var runner = new TrackingProcessRunner
        {
            WgLatestHandshakes = $"key-a\t{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10}\n",
        };
        var probe = new TunnelHealthProbe(runner, NullLogger<TunnelHealthProbe>.Instance);

        var health = await probe.CheckAsync(Profile(), "wg0", CancellationToken.None);

        Assert.Equal(TunnelHealthState.Healthy, health.State);
        Assert.True(health.AllowsSplitDns);
    }

    [Fact]
    public async Task CheckAsync_AmneziaWg_UsesAwgShow()
    {
        var runner = new TrackingProcessRunner
        {
            AwgLatestHandshakes = $"key-a\t{DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 12}\n",
        };
        var probe = new TunnelHealthProbe(runner, NullLogger<TunnelHealthProbe>.Instance);

        var health = await probe.CheckAsync(
            Profile("awg0", BackendKind.AmneziaWg),
            "awg0",
            CancellationToken.None);

        Assert.Equal(TunnelHealthState.Healthy, health.State);
        Assert.Contains(
            runner.Commands,
            c => c.FileName == "awg" && c.Arguments.SequenceEqual(["show", "awg0", "latest-handshakes"]));
        Assert.DoesNotContain(runner.Commands, static c => c.FileName == "wg");
    }

    private static VpnProfile Profile(string iface = "wg0", BackendKind backend = BackendKind.Native) =>
        VpnProfile.Create("p", backend, iface);
}
