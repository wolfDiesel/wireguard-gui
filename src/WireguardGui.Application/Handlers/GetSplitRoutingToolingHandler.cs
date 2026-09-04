using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;

namespace WireguardGui.Application.Handlers;

public sealed class GetSplitRoutingToolingHandler(ISystemCapabilityProbe probe)
{
    public SplitRoutingToolingDto Handle()
    {
        var tooling = probe.ProbeSplitRoutingTooling();
        return new SplitRoutingToolingDto(
            tooling.HasIp,
            tooling.HasDig,
            tooling.HasResolvectl,
            tooling.MissingCommands,
            tooling.FedoraInstallHint,
            tooling.DebianInstallHint,
            tooling.HasMissingCommands,
            tooling.PolicyRoutingAvailable,
            tooling.DnsMonitorAvailable,
            tooling.DomainResolveAvailable);
    }
}
