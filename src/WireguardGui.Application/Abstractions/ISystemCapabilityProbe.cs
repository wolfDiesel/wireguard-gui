using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public sealed record BackendCapability(
    BackendKind Backend,
    bool IsAvailable,
    IReadOnlyList<string> MissingComponents,
    string FedoraInstallHint,
    string DebianInstallHint);

public interface ISystemCapabilityProbe
{
    Task<BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default);
    Task<BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default);
}
