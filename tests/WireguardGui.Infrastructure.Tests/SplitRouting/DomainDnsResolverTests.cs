using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Tests.Fakes;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class DomainDnsResolverTests
{
    [Fact]
    public async Task ResolveIpv4Async_TunnelDnsAnswer_SkipsSystemResolver()
    {
        var runner = new FakeProcessRunner
        {
            TunnelDigResponses =
            {
                ["8.8.8.8|A|rutracker.org"] = "104.21.32.39\n172.67.182.196\n",
            },
        };
        var resolver = CreateResolver(runner);

        var addresses = await resolver.ResolveIpv4Async("rutracker.org", ["8.8.8.8"], CancellationToken.None);

        Assert.Equal(["104.21.32.39", "172.67.182.196"], addresses);
        Assert.DoesNotContain(runner.DigCalls, static call => call.Server is null);
    }

    [Fact]
    public async Task ResolveIpv4Async_TunnelDnsWithoutAnswer_FallsBackToSystemResolver()
    {
        var runner = new FakeProcessRunner
        {
            DigResponses = { ["cults3d.com"] = "172.67.75.246\n" },
        };
        var resolver = CreateResolver(runner);

        var addresses = await resolver.ResolveIpv4Async("cults3d.com", ["8.8.8.8"], CancellationToken.None);

        Assert.Equal(["172.67.75.246"], addresses);
        Assert.Contains(runner.DigCalls, static call => call is { Server: "8.8.8.8", RecordType: "A" });
        Assert.Contains(runner.DigCalls, static call => call.Server is null);
    }

    [Fact]
    public async Task ResolveIpv4Async_FirstTunnelServerEmpty_TriesNextOne()
    {
        var runner = new FakeProcessRunner
        {
            TunnelDigResponses =
            {
                ["9.9.9.9|A|kinozal.me"] = "172.67.142.133",
            },
        };
        var resolver = CreateResolver(runner);

        var addresses = await resolver.ResolveIpv4Async("kinozal.me", ["8.8.8.8", "9.9.9.9"], CancellationToken.None);

        Assert.Equal(["172.67.142.133"], addresses);
        Assert.Equal(
            ["8.8.8.8", "9.9.9.9"],
            runner.DigCalls.Where(static call => call.Server is not null).Select(static call => call.Server!).ToArray());
        Assert.DoesNotContain(runner.DigCalls, static call => call.Server is null);
    }

    [Fact]
    public async Task ResolveIpv4Async_TunnelQuery_UsesShortTimeoutAndSingleAttempt()
    {
        var runner = new FakeProcessRunner();
        var resolver = CreateResolver(runner);

        await resolver.ResolveIpv4Async("rutracker.org", ["8.8.8.8"], CancellationToken.None);

        Assert.Contains(runner.DigCalls, static call => call is { Server: "8.8.8.8", Domain: "rutracker.org" });
        Assert.Equal("8.8.8.8", runner.DigCalls[0].Server);
        Assert.Equal("A", runner.DigCalls[0].RecordType);
        Assert.Contains("+time=2", runner.RawDigArguments[0]);
        Assert.Contains("+tries=1", runner.RawDigArguments[0]);
        Assert.Contains("@8.8.8.8", runner.RawDigArguments[0]);
    }

    [Fact]
    public async Task ResolveIpv4Async_DigFails_ReturnsEmptyWithoutThrowing()
    {
        var runner = new FakeProcessRunner { DigFailureExitCode = 2 };
        var resolver = CreateResolver(runner);

        var addresses = await resolver.ResolveIpv4Async("rutracker.org", ["8.8.8.8"], CancellationToken.None);

        Assert.Empty(addresses);
    }

    [Fact]
    public async Task ResolveIpv6Async_TunnelDnsAnswer_ReturnsIpv6Only()
    {
        var runner = new FakeProcessRunner
        {
            TunnelDigResponses =
            {
                ["8.8.8.8|AAAA|rutracker.org"] = "2606:4700:20::681a:cd7\n",
                ["8.8.8.8|A|rutracker.org"] = "104.21.32.39\n",
            },
        };
        var resolver = CreateResolver(runner);

        var addresses = await resolver.ResolveIpv6Async("rutracker.org", ["8.8.8.8"], CancellationToken.None);

        Assert.Equal(["2606:4700:20::681a:cd7"], addresses);
    }

    [Fact]
    public async Task ResolveIpv4Async_NoUpstream_UsesSystemResolverOnly()
    {
        var runner = new FakeProcessRunner
        {
            DigResponses = { ["twitch.tv"] = "151.101.66.167\n" },
        };
        var resolver = CreateResolver(runner);

        var addresses = await resolver.ResolveIpv4Async("twitch.tv", CancellationToken.None);

        Assert.Equal(["151.101.66.167"], addresses);
        Assert.All(runner.DigCalls, static call => Assert.Null(call.Server));
    }

    private static DomainDnsResolver CreateResolver(FakeProcessRunner runner) =>
        new(runner, NullLogger<DomainDnsResolver>.Instance);
}
