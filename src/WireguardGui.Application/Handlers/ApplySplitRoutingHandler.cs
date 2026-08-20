using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;
using WireguardGui.Application.Services;

namespace WireguardGui.Application.Handlers;

public sealed class ApplySplitRoutingHandler(
    IProfileStore profileStore,
    ISplitRoutingConfigUpdater splitRoutingConfigUpdater,
    IPolicyRoutingSetup policyRoutingSetup,
    IWireGuardBackendFactory backendFactory,
    ILogger<ApplySplitRoutingHandler> logger)
{
    public async Task<SplitRoutingResultDto> HandleAsync(
        string profileId,
        IProgress<SplitRoutingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await HandleCoreAsync(profileId, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Apply split routing failed for {ProfileId}", profileId);
            return new SplitRoutingResultDto(false, 0, null, ex.Message);
        }
    }

    private async Task<SplitRoutingResultDto> HandleCoreAsync(
        string profileId,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new SplitRoutingResultDto(false, 0, null, "Profile not found");

        if (!profile.SplitRouting.Enabled)
            return new SplitRoutingResultDto(false, 0, null, "Split routing is disabled");

        logger.LogInformation("Applying split routing for {Profile}", profile.Name);

        var backend = backendFactory.GetBackend(profile.Backend);
        var wasConnected = await backend.GetConnectionStateAsync(profile, cancellationToken) == ConnectionState.Connected;
        if (wasConnected && !policyRoutingSetup.IsAvailable)
            progress?.Report(new SplitRoutingProgress("Progress_Reconnect_Required"));

        var configUpdate = await splitRoutingConfigUpdater.TryUpdateConfigAsync(
            profile,
            progress,
            cancellationToken);
        if (configUpdate.ErrorMessage is not null)
            return new SplitRoutingResultDto(false, 0, null, configUpdate.ErrorMessage);

        if (configUpdate.UsesPolicyRouting)
            return await ApplyPolicyRoutingAsync(
                profileId,
                profile,
                backend,
                configUpdate,
                wasConnected,
                progress,
                cancellationToken);

        return await ApplyLegacyRoutingAsync(
            profileId,
            profile,
            backend,
            configUpdate,
            wasConnected,
            progress,
            cancellationToken);
    }

    private async Task<SplitRoutingResultDto> ApplyPolicyRoutingAsync(
        string profileId,
        VpnProfile profile,
        IWireGuardBackend backend,
        SplitRoutingConfigUpdateResult configUpdate,
        bool wasConnected,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!wasConnected)
        {
            progress?.Report(new SplitRoutingProgress(
                configUpdate.Changed ? "Progress_Routes_Written" : "Progress_Routes_Unchanged",
                configUpdate.RouteCount.ToString()));
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
        }

        if (configUpdate.Changed)
        {
            progress?.Report(new SplitRoutingProgress("Progress_Reconnect_Required"));
            logger.LogInformation(
                "Split routing {Profile}: reconnecting after policy baseline update",
                profile.Name);

            try
            {
                await backend.ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);
            }
            catch (WireGuardOperationException ex)
            {
                profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
                var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
                return ConnectionOutcomeResolver.ResolveSplitRoutingAfterFailure(
                    state,
                    configUpdate.RouteCount,
                    configUpdate.RoutesCsv,
                    ex.UserMessage);
            }
        }

        var syncResult = await policyRoutingSetup.SyncRoutesAsync(
            profile,
            configUpdate.Routes ?? [],
            cancellationToken);
        if (syncResult.ErrorMessage is not null)
            return new SplitRoutingResultDto(false, configUpdate.RouteCount, null, syncResult.ErrorMessage);

        progress?.Report(new SplitRoutingProgress(
            syncResult.RoutesChanged ? "Progress_Done" : "Progress_Routes_Unchanged",
            configUpdate.RouteCount.ToString()));
        return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
    }

    private async Task<SplitRoutingResultDto> ApplyLegacyRoutingAsync(
        string profileId,
        VpnProfile profile,
        IWireGuardBackend backend,
        SplitRoutingConfigUpdateResult configUpdate,
        bool wasConnected,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!configUpdate.Changed)
        {
            logger.LogInformation(
                "Split routing {Profile}: config unchanged ({Count} routes)",
                profile.Name,
                configUpdate.RouteCount);
            progress?.Report(new SplitRoutingProgress("Progress_Routes_Unchanged"));
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, null, null);
        }

        if (!wasConnected)
        {
            progress?.Report(new SplitRoutingProgress("Progress_Routes_Written", configUpdate.RouteCount.ToString()));
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
        }

        progress?.Report(new SplitRoutingProgress("Progress_Reconnect_Nm"));
        logger.LogInformation("Split routing {Profile}: reconnecting after route update", profile.Name);

        try
        {
            await backend.ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);
            progress?.Report(new SplitRoutingProgress("Progress_Done", configUpdate.RouteCount.ToString()));
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
        }
        catch (WireGuardOperationException ex)
        {
            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            return ConnectionOutcomeResolver.ResolveSplitRoutingAfterFailure(
                state,
                configUpdate.RouteCount,
                configUpdate.RoutesCsv,
                ex.UserMessage);
        }
    }
}
