using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IPolicyRoutingSetup
{
    bool IsAvailable { get; }

    Task<PolicyRoutingApplyResult> ApplyAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default);

    Task<PolicyRoutingSyncResult> SyncRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default);

    Task TeardownAsync(VpnProfile profile, CancellationToken cancellationToken = default);

    Task PrepareConnectionAsync(VpnProfile profile, CancellationToken cancellationToken = default);
}

public sealed record PolicyRoutingApplyResult(bool Success, string? ErrorMessage);

public sealed record PolicyRoutingSyncResult(bool RoutesChanged, string? ErrorMessage);
