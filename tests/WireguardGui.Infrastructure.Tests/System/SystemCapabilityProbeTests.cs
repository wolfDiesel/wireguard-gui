using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.System;

namespace WireguardGui.Infrastructure.Tests.System;

public class SystemCapabilityProbeTests
{
    [Fact]
    public async Task ProbeNative_ReturnsUnavailable_WhenWgMissing()
    {
        var probe = new SystemCapabilityProbe(new FakeProcessRunner(["nmcli"]));
        var native = await probe.ProbeNativeAsync();
        Assert.False(native.IsAvailable);
        Assert.Contains("wg", native.MissingComponents);
    }

    [Fact]
    public async Task ProbeNmcli_ReturnsUnavailable_WhenNmcliMissing()
    {
        var probe = new SystemCapabilityProbe(new FakeProcessRunner(["wg", "wg-quick"]));
        var nmcli = await probe.ProbeNmcliAsync();
        Assert.False(nmcli.IsAvailable);
        Assert.Contains("nmcli", nmcli.MissingComponents);
    }

    [Fact]
    public void ProbeSplitRoutingTooling_ReportsMissingCommands()
    {
        var probe = new SystemCapabilityProbe(new FakeProcessRunner(["ip"]));
        var tooling = probe.ProbeSplitRoutingTooling();

        Assert.True(tooling.HasIp);
        Assert.False(tooling.HasDig);
        Assert.False(tooling.HasResolvectl);
        Assert.True(tooling.HasMissingCommands);
        Assert.Contains("dig", tooling.MissingCommands);
        Assert.Contains("resolvectl", tooling.MissingCommands);
        Assert.False(tooling.DnsMonitorAvailable);
        Assert.False(tooling.DomainResolveAvailable);
        Assert.Contains("bind-utils", tooling.FedoraInstallHint, StringComparison.Ordinal);
        Assert.Contains("dnsutils", tooling.DebianInstallHint, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeSplitRoutingTooling_AllPresent()
    {
        var probe = new SystemCapabilityProbe(new FakeProcessRunner(["ip", "dig", "resolvectl"]));
        var tooling = probe.ProbeSplitRoutingTooling();

        Assert.False(tooling.HasMissingCommands);
        Assert.True(tooling.DnsMonitorAvailable);
        Assert.True(tooling.DomainResolveAvailable);
        Assert.Empty(tooling.FedoraInstallHint);
    }

    [Fact]
    public async Task ProbeAmneziaWg_Available_WithKernelModule()
    {
        var probe = new ProbeWithModules(new FakeProcessRunner(["awg", "awg-quick"]), "amneziawg");
        var awg = await probe.ProbeAmneziaWgAsync();

        Assert.True(awg.IsAvailable);
        Assert.Empty(awg.MissingComponents);
    }

    [Fact]
    public async Task ProbeAmneziaWg_Available_WithUserspaceTransport()
    {
        var probe = new ProbeWithModules(new FakeProcessRunner(["awg", "awg-quick", "amneziawg-go"]));
        var awg = await probe.ProbeAmneziaWgAsync();

        Assert.True(awg.IsAvailable);
    }

    [Fact]
    public async Task ProbeAmneziaWg_Unavailable_WithoutTools()
    {
        var probe = new ProbeWithModules(new FakeProcessRunner(["amneziawg-go"]));
        var awg = await probe.ProbeAmneziaWgAsync();

        Assert.False(awg.IsAvailable);
        Assert.Contains("awg", awg.MissingComponents);
        Assert.Contains("awg-quick", awg.MissingComponents);
        Assert.Contains("amneziawg", awg.FedoraInstallHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("amneziawg", awg.DebianInstallHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAmneziaWg_Unavailable_WithoutTransport()
    {
        var probe = new ProbeWithModules(new FakeProcessRunner(["awg", "awg-quick"]));
        var awg = await probe.ProbeAmneziaWgAsync();

        Assert.False(awg.IsAvailable);
        Assert.Contains("amneziawg kernel module or amneziawg-go", awg.MissingComponents);
    }

    [Fact]
    public async Task ProbeAll_ReturnsAllThreeBackends()
    {
        var probe = new ProbeWithModules(new FakeProcessRunner(["nmcli"]));
        var all = await probe.ProbeAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, static capability => capability.Backend == BackendKind.AmneziaWg);
    }

    private sealed class FakeProcessRunner(IReadOnlyList<string> available) : IProcessRunner
    {
        public bool IsCommandAvailable(string command) => available.Contains(command);

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, "running", string.Empty));

        public Task<ProcessResult> RunPrivilegedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, cancellationToken);

        public Task<ProcessResult> RunPrivilegedShellAsync(string script, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public bool HasActivePrivilegedSession { get; set; }
    }

    private sealed class ProbeWithModules : SystemCapabilityProbe
    {
        private readonly HashSet<string> _modules;

        public ProbeWithModules(IProcessRunner runner, params string[] modules)
            : base(runner) =>
            _modules = [.. modules];

        protected override bool IsKernelModulePresent(string moduleName) =>
            _modules.Contains(moduleName);
    }
}
