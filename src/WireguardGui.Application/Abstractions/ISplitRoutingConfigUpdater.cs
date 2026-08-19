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
    string? ErrorMessage,
    IReadOnlyList<string>? Routes = null,
    bool UsesPolicyRouting = false);

public static class SplitRoutingPolicy
{
    public const bool RemoveDnsOnApply = true;
    public const string PolicyAllowedIps = "0.0.0.0/0";
    public const string PolicyTable = "off";
}
