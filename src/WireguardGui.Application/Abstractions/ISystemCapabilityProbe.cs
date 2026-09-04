using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public sealed record BackendCapability(
    BackendKind Backend,
    bool IsAvailable,
    IReadOnlyList<string> MissingComponents,
    string FedoraInstallHint,
    string DebianInstallHint);

public sealed record SplitRoutingToolingCapability(
    bool HasIp,
    bool HasDig,
    bool HasResolvectl,
    IReadOnlyList<string> MissingCommands,
    string FedoraInstallHint,
    string DebianInstallHint)
{
    public bool HasMissingCommands => MissingCommands.Count > 0;

    public bool PolicyRoutingAvailable => HasIp;

    public bool DnsMonitorAvailable => HasIp && HasResolvectl;

    public bool DomainResolveAvailable => HasDig;
}

public interface ISystemCapabilityProbe
{
    Task<BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default);
    Task<BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default);
    SplitRoutingToolingCapability ProbeSplitRoutingTooling();
}
