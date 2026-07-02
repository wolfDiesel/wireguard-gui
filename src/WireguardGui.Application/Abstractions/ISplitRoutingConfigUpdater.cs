namespace WireguardGui.Application.Abstractions;

using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

public interface ISplitRoutingConfigUpdater
{
    Task<SplitRoutingConfigUpdateResult> TryUpdateConfigAsync(
        VpnProfile profile,
        IProgress<SplitRoutingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record SplitRoutingConfigUpdateResult(
    bool Changed,
    int RouteCount,
    string? RoutesCsv,
    string? ErrorMessage);

public static class SplitRoutingPolicy
{
    public const bool RemoveDnsOnApply = true;
}
