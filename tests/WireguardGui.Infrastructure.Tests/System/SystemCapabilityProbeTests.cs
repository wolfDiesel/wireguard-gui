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

    private sealed class FakeProcessRunner(IReadOnlyList<string> available) : IProcessRunner
    {
        public bool IsCommandAvailable(string command) => available.Contains(command);

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, "running", string.Empty));

        public Task<ProcessResult> RunPrivilegedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, cancellationToken);

        public Task<ProcessResult> RunPrivilegedShellAsync(string script, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}
