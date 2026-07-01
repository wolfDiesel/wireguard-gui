using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IWireGuardBackend
{
    BackendKind Kind { get; }
    Task ImportAsync(VpnProfile profile, string configPath, CancellationToken cancellationToken = default);
    Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default);
    Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default);
    Task<ConnectionState> GetConnectionStateAsync(VpnProfile profile, CancellationToken cancellationToken = default);
    Task ApplyRoutesAsync(VpnProfile profile, IReadOnlyList<string> routes, CancellationToken cancellationToken = default);
    Task ReimportFromConfigAsync(
        VpnProfile profile,
        bool connectAfter,
        CancellationToken cancellationToken = default);
    Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default);
}

public interface IWireGuardBackendFactory
{
    IWireGuardBackend GetBackend(BackendKind kind);
}
