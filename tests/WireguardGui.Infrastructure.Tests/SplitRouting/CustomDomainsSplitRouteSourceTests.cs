using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.SplitRouting.Sources;
using WireguardGui.Infrastructure.Tests.Fakes;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class CustomDomainsSplitRouteSourceTests
{
    [Theory]
    [InlineData("instagram.com", new[] { "instagram.com", "www.instagram.com" })]
    [InlineData("www.instagram.com", new[] { "www.instagram.com" })]
    public void ExpandDomainVariants_IncludesWww(string domain, string[] expected) =>
        Assert.Equal(expected, CustomDomainsSplitRouteSource.ExpandDomainVariants(domain).ToArray());

    [Theory]
    [InlineData("157.240.0.0/16", true, "157.240.0.0/16")]
    [InlineData("57.144.244.34/32", true, "57.144.244.34/32")]
    [InlineData("2001:db8::/32", true, "2001:db8::/32")]
    [InlineData("instagram.com", false, "")]
    [InlineData("1.2.3.4/99", false, "")]
    public void TryParseStaticRoute_AcceptsCidrOnly(string entry, bool expected, string route)
    {
        var parsed = CustomDomainsSplitRouteSource.TryParseStaticRoute(entry, out var value);
        Assert.Equal(expected, parsed);
        Assert.Equal(route, value);
    }

    [Fact]
    public async Task CollectAsync_DomainBlockedLocally_ResolvesThroughTunnelDns()
    {
        var runner = new FakeProcessRunner
        {
            TunnelDigResponses =
            {
                ["8.8.8.8|A|rutracker.org"] = "104.21.32.39\n172.67.182.196\n",
                ["8.8.8.8|A|www.rutracker.org"] = "104.21.32.39\n",
            },
        };
        var source = CreateSource(runner);
        var settings = CreateSettings(["rutracker.org"]);

        var routes = await source.CollectAsync(settings, null, CancellationToken.None);

        Assert.Contains("104.21.32.39/32", routes);
        Assert.Contains("172.67.182.196/32", routes);
        Assert.All(routes, static route => Assert.EndsWith("/32", route));
        Assert.DoesNotContain(runner.DigCalls, static call => call is { Server: null, RecordType: "A" });
    }

    [Fact]
    public async Task CollectAsync_TunnelDnsSilent_FallsBackToSystemResolver()
    {
        var runner = new FakeProcessRunner
        {
            DigResponses = { ["kinozal.me"] = "172.67.142.133\n" },
        };
        var source = CreateSource(runner);

        var routes = await source.CollectAsync(CreateSettings(["kinozal.me"]), null, CancellationToken.None);

        Assert.Contains("172.67.142.133/32", routes);
        Assert.Contains(runner.DigCalls, static call => call.Server is null);
    }

    [Fact]
    public async Task CollectAsync_NoAnswersAnywhere_ReturnsNoRoutesAndKeepsGoing()
    {
        var runner = new FakeProcessRunner();
        var source = CreateSource(runner);

        var routes = await source.CollectAsync(
            CreateSettings(["rutracker.org", "kinozal.me"]),
            null,
            CancellationToken.None);

        Assert.Empty(routes);
        Assert.Contains(runner.DigCalls, static call => call is { Server: "8.8.8.8", Domain: "rutracker.org" });
        Assert.Contains(runner.DigCalls, static call => call is { Server: "8.8.8.8", Domain: "kinozal.me" });
    }

    [Fact]
    public async Task CollectAsync_StaticCidr_DoesNotQueryDns()
    {
        var runner = new FakeProcessRunner();
        var source = CreateSource(runner);

        var routes = await source.CollectAsync(CreateSettings(["10.0.0.0/8"]), null, CancellationToken.None);

        Assert.Equal(["10.0.0.0/8"], routes);
        Assert.Empty(runner.DigCalls);
    }

    private static CustomDomainsSplitRouteSource CreateSource(FakeProcessRunner runner) =>
        new(
            new DomainDnsResolver(runner, NullLogger<DomainDnsResolver>.Instance),
            NullLogger<CustomDomainsSplitRouteSource>.Instance);

    private static SplitRoutingSettings CreateSettings(string[] domains) =>
        SplitRoutingSettings.CreateDefault() with
        {
            Enabled = true,
            CustomDomains = domains,
            TunnelDns = "8.8.8.8",
        };
}
