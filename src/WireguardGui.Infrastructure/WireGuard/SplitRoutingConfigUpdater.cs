using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class SplitRoutingConfigUpdater(
    IProfileStore profileStore,
    ISplitRouteBuilder splitRouteBuilder,
    IWireGuardConfigParser configParser,
    IPolicyRoutingSetup policyRoutingSetup,
    ILogger<SplitRoutingConfigUpdater> logger) : ISplitRoutingConfigUpdater
{
    public async Task<SplitRoutingConfigUpdateResult> TryUpdateConfigAsync(
        VpnProfile profile,
        IProgress<SplitRoutingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!profile.SplitRouting.Enabled)
            return new SplitRoutingConfigUpdateResult(false, 0, null, null);

        logger.LogInformation("Updating split routing config for {Profile}", profile.Name);

        IReadOnlyList<string> routes;
        try
        {
            routes = await splitRouteBuilder.BuildRoutesAsync(profile.SplitRouting, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WireGuardOperationException ex)
        {
            logger.LogWarning("Route scan for {Profile} failed: {Message}", profile.Name, ex.UserMessage);
            return new SplitRoutingConfigUpdateResult(false, 0, null, ex.UserMessage);
        }

        if (routes.Count == 0)
        {
            logger.LogWarning("Route scan for {Profile}: no routes found", profile.Name);
            return new SplitRoutingConfigUpdateResult(false, 0, null, "No routes were generated");
        }

        var routesCsv = string.Join(",", routes);
        var configPath = profileStore.GetConfigPath(profile);
        var configContent = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);

        if (policyRoutingSetup.IsAvailable)
            return await TryUpdatePolicyBaselineAsync(
                profile,
                routes,
                routesCsv,
                configPath,
                configContent,
                progress,
                cancellationToken).ConfigureAwait(false);

        return await TryUpdateLegacyAllowedIpsAsync(
            profile,
            routes,
            routesCsv,
            configPath,
            configContent,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SplitRoutingConfigUpdateResult> TryUpdatePolicyBaselineAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        string routesCsv,
        string configPath,
        string configContent,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (configParser.IsPolicySplitBaseline(configContent))
        {
            logger.LogInformation(
                "Config {Profile}: policy baseline unchanged ({Count} routes)",
                profile.Name,
                routes.Count);
            return new SplitRoutingConfigUpdateResult(
                false,
                routes.Count,
                routesCsv,
                null,
                routes,
                UsesPolicyRouting: true);
        }

        progress?.Report(new SplitRoutingProgress("Progress_Write_Config"));

        var updated = configParser.EnsurePolicySplitBaseline(configContent);
        await File.WriteAllTextAsync(configPath, updated, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Config {Profile}: switched to policy split baseline ({Count} live routes)",
            profile.Name,
            routes.Count);

        return new SplitRoutingConfigUpdateResult(
            true,
            routes.Count,
            routesCsv,
            null,
            routes,
            UsesPolicyRouting: true);
    }

    private async Task<SplitRoutingConfigUpdateResult> TryUpdateLegacyAllowedIpsAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        string routesCsv,
        string configPath,
        string configContent,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedNew = configParser.NormalizeAllowedIps(routesCsv);
        var normalizedOld = configParser.NormalizeAllowedIps(configParser.ReadAllowedIps(configContent));
        var dnsPresent = configParser.HasDns(configContent);

        if (normalizedNew == normalizedOld && !dnsPresent)
        {
            logger.LogInformation(
                "Config {Profile}: AllowedIPs unchanged ({Count} routes)",
                profile.Name,
                routes.Count);
            return new SplitRoutingConfigUpdateResult(false, routes.Count, routesCsv, null);
        }

        progress?.Report(new SplitRoutingProgress("Progress_Write_Config"));

        var updated = configParser.WriteAllowedIps(configContent, routesCsv);
        if (dnsPresent && SplitRoutingPolicy.RemoveDnsOnApply)
            updated = configParser.RemoveDns(updated);

        await File.WriteAllTextAsync(configPath, updated, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Config {Profile}: updated AllowedIPs ({Count} routes), DNS removed={DnsRemoved}",
            profile.Name,
            routes.Count,
            dnsPresent && SplitRoutingPolicy.RemoveDnsOnApply);

        return new SplitRoutingConfigUpdateResult(true, routes.Count, routesCsv, null);
    }
}
