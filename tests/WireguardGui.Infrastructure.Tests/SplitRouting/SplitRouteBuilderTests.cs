using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.System;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class SplitRouteBuilderTests
{
    [Fact]
    public async Task BuildRoutes_IncludesTelegramAndCloudflare()
    {
        var runner = new FakeProcessRunner();
        var builder = new SplitRouteBuilder(runner, new HttpClient(), NullLogger<SplitRouteBuilder>.Instance);
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: true,
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
        var builder = new SplitRouteBuilder(runner, new HttpClient(), NullLogger<SplitRouteBuilder>.Instance);
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: false,
            CustomDomains: ["example.com"],
            IncludeCloudflare: false,
            MaxRoutes: 200);

        var routes = await builder.BuildRoutesAsync(settings);
        Assert.Contains("93.184.216.34/32", routes);
    }

    [Fact]
    public async Task BuildRoutes_RespectsMaxRoutes()
    {
        var runner = new FakeProcessRunner();
        var builder = new SplitRouteBuilder(runner, new HttpClient(), NullLogger<SplitRouteBuilder>.Instance);
        var settings = new SplitRoutingSettings(
            Enabled: true,
            Youtube: false,
            Telegram: true,
            CustomDomains: [],
            IncludeCloudflare: true,
            MaxRoutes: 3);

        var routes = await builder.BuildRoutesAsync(settings);
        Assert.Equal(3, routes.Count);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Dictionary<string, string> DigResponses { get; init; } = new();

        public bool IsCommandAvailable(string command) => command is "dig" or "wg" or "wg-quick" or "nmcli";

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            if (fileName == "dig" && arguments.Count >= 3)
            {
                var domain = arguments[^1];
                var output = DigResponses.GetValueOrDefault(domain, string.Empty);
                return Task.FromResult(new ProcessResult(0, output, string.Empty));
            }

            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }

        public Task<ProcessResult> RunPrivilegedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, cancellationToken);

        public Task<ProcessResult> RunPrivilegedShellAsync(string script, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}
