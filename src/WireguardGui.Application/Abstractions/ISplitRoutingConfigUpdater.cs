namespace WireguardGui.Application.Abstractions;

using WireguardGui.Domain;

public interface ISplitRoutingConfigUpdater
{
    Task<SplitRoutingConfigUpdateResult> TryUpdateConfigAsync(
        VpnProfile profile,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record SplitRoutingConfigUpdateResult(
    bool Changed,
    int RouteCount,
    string? RoutesCsv,
    string? ErrorMessage);
