using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.SplitRouting.Sources;

namespace WireguardGui.Infrastructure.Tests.Fakes;

internal static class SplitRouteSourceTestFactory
{
    public static TelegramSplitRouteSource CreateTelegramSource(
        FakeProcessRunner runner,
        bool policyAvailable = false) =>
        new(new DomainDnsResolver(runner), new FakePolicyRoutingSetup { IsAvailable = policyAvailable });
}

internal sealed class FakePolicyRoutingSetup : IPolicyRoutingSetup
{
    public bool IsAvailable { get; init; }

    public List<IReadOnlyList<string>> AppliedRoutes { get; } = [];
    public List<IReadOnlyList<string>> SyncedRoutes { get; } = [];
    public List<string> TeardownProfileIds { get; } = [];

    public Task PrepareConnectionAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<PolicyRoutingApplyResult> ApplyAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default)
    {
        AppliedRoutes.Add(routes);
        return Task.FromResult(new PolicyRoutingApplyResult(true, null));
    }

    public Task<PolicyRoutingSyncResult> SyncRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default)
    {
        SyncedRoutes.Add(routes);
        return Task.FromResult(new PolicyRoutingSyncResult(true, null));
    }

    public Task TeardownAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        TeardownProfileIds.Add(profile.Id);
        return Task.CompletedTask;
    }
}

internal sealed class TrackingProcessRunner : IProcessRunner
{
    public List<string> PrivilegedShellScripts { get; } = [];
    public List<(string FileName, string[] Arguments)> PrivilegedCommands { get; } = [];

    public bool IpAvailable { get; init; } = true;
    public bool InterfaceIpv6Capable { get; init; } = true;
    public bool FailRouteFlush { get; init; }
    public string WgInterfaces { get; init; } = "wg0";
    public string DefaultRoute { get; init; } = "default via 192.168.1.1 dev eth0";

    public bool IsCommandAvailable(string command) => command switch
    {
        "ip" => IpAvailable,
        "dig" => true,
        "wg" => true,
        "nmcli" => true,
        _ => false,
    };

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (fileName == "wg" && arguments.Count >= 2 && arguments[0] == "show" && arguments[1] == "interfaces")
            return Task.FromResult(new ProcessResult(0, WgInterfaces, string.Empty));

        if (fileName == "ip" && arguments.Count >= 4 &&
            string.Equals(arguments[0], "-6", StringComparison.Ordinal) &&
            string.Equals(arguments[1], "addr", StringComparison.Ordinal) &&
            string.Equals(arguments[2], "show", StringComparison.Ordinal) &&
            string.Equals(arguments[3], "dev", StringComparison.Ordinal))
        {
            var output = InterfaceIpv6Capable
                ? "2: wg0: <POINTOPOINT,NOARP,UP,LOWER_UP> mtu 1420\n    inet6 fe80::1/64 scope link\n"
                : string.Empty;
            return Task.FromResult(new ProcessResult(0, output, string.Empty));
        }

        if (fileName == "ip" && arguments.Count >= 2 && arguments[0] == "route" && arguments[1] == "show")
            return Task.FromResult(new ProcessResult(0, DefaultRoute, string.Empty));

        if (fileName == "dig")
        {
            if (arguments.Any(a => string.Equals(a, "AAAA", StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
            return Task.FromResult(new ProcessResult(0, "1.2.3.4\n", string.Empty));
        }

        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    public Task<ProcessResult> RunPrivilegedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        PrivilegedCommands.Add((fileName, arguments.ToArray()));
        if (FailRouteFlush &&
            fileName == "ip" &&
            arguments.Count >= 2 &&
            string.Equals(arguments[0], "route", StringComparison.Ordinal) &&
            string.Equals(arguments[1], "flush", StringComparison.Ordinal))
        {
            return Task.FromResult(new ProcessResult(2, string.Empty, "Error: ipv4: FIB table does not exist.\nFlush terminated\n"));
        }

        return RunAsync(fileName, arguments, cancellationToken);
    }

    public Task<ProcessResult> RunPrivilegedShellAsync(string script, CancellationToken cancellationToken = default)
    {
        PrivilegedShellScripts.Add(script);
        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}
