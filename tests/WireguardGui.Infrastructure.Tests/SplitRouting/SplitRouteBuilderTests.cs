using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.SplitRouting.Sources;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.Tests.Fakes;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class SplitRouteBuilderTests
{
    [Fact]
    public async Task BuildRoutes_IncludesTelegramAndCloudflare()
    {
        var builder = CreateBuilder(new FakeProcessRunner());
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: true,
            Twitch: false,
            CustomDomains: [],
            IncludeCloudflare: true,
            MaxRoutes: 200);

        var routes = await builder.BuildRoutesAsync(settings);

        Assert.Contains("149.154.160.0/20", routes);
        Assert.Contains("104.16.0.0/12", routes);
    }

    [Fact]
    public async Task BuildRoutes_ResolvesDomains()
    {
        var runner = new FakeProcessRunner
        {
            DigResponses = new Dictionary<string, string>
            {
                ["example.com"] = "93.184.216.34\n",
            },
        };
        var builder = CreateBuilder(runner);
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: false,
            Twitch: false,
            CustomDomains: ["example.com"],
            IncludeCloudflare: false,
            MaxRoutes: 200);

        var routes = await builder.BuildRoutesAsync(settings);
        Assert.Contains("93.184.216.34/32", routes);
    }

    [Fact]
    public async Task BuildRoutes_ResolvesTwitchDomains()
    {
        var runner = new FakeProcessRunner
        {
            DigResponses = new Dictionary<string, string>
            {
                ["twitch.tv"] = "151.101.1.11\n",
            },
        };
        var builder = CreateBuilder(runner);
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: false,
            Twitch: true,
            CustomDomains: [],
            IncludeCloudflare: false,
            MaxRoutes: 200);

        var routes = await builder.BuildRoutesAsync(settings);
        Assert.Contains("151.101.1.11/32", routes);
    }

    [Fact]
    public async Task BuildRoutes_TwitchOff_SkipsTwitchDomains()
    {
        var runner = new FakeProcessRunner
        {
            DigResponses = new Dictionary<string, string>
            {
                ["twitch.tv"] = "151.101.1.11\n",
            },
        };
        var builder = CreateBuilder(runner);
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: false,
            Twitch: false,
            CustomDomains: [],
            IncludeCloudflare: false,
            MaxRoutes: 200);

        var routes = await builder.BuildRoutesAsync(settings);
        Assert.DoesNotContain("151.101.1.11/32", routes);
    }

    [Fact]
    public async Task BuildRoutes_RespectsMaxRoutes_PrefersStaticSources()
    {
        var builder = CreateBuilder(new FakeProcessRunner());
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: true,
            Twitch: false,
            CustomDomains: [],
            IncludeCloudflare: true,
            MaxRoutes: 3);

        var routes = await builder.BuildRoutesAsync(settings);
        Assert.Equal(3, routes.Count);
        Assert.Contains("149.154.160.0/20", routes);
    }

    private static SplitRouteBuilder CreateBuilder(FakeProcessRunner runner)
    {
        var dns = new DomainDnsResolver(runner);
        var paths = new AppDataPaths();
        ISplitRouteSource[] sources =
        [
            new TelegramSplitRouteSource(),
            new CloudflareSplitRouteSource(),
            new CustomDomainsSplitRouteSource(dns, NullLogger<CustomDomainsSplitRouteSource>.Instance),
            new TwitchSplitRouteSource(dns, NullLogger<TwitchSplitRouteSource>.Instance),
            new YouTubeSplitRouteSource(new HttpClient(), paths, NullLogger<YouTubeSplitRouteSource>.Instance),
        ];
        return new SplitRouteBuilder(sources, NullLogger<SplitRouteBuilder>.Instance);
    }
}
