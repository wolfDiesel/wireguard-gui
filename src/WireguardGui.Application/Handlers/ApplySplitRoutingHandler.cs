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
    IWireGuardBackendFactory backendFactory,
    ILogger<ApplySplitRoutingHandler> logger)
{
    public async Task<SplitRoutingResultDto> HandleAsync(
        string profileId,
        IProgress<SplitRoutingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new SplitRoutingResultDto(false, 0, null, "Profile not found");

        if (!profile.SplitRouting.Enabled)
            return new SplitRoutingResultDto(false, 0, null, "Split routing is disabled");

        logger.LogInformation("Applying split routing for {Profile}", profile.Name);

        var backend = backendFactory.GetBackend(profile.Backend);
        var wasConnected = await backend.GetConnectionStateAsync(profile, cancellationToken) == ConnectionState.Connected;
        if (wasConnected)
            progress?.Report(new SplitRoutingProgress("Progress_Reconnect_Required"));

        var configUpdate = await splitRoutingConfigUpdater.TryUpdateConfigAsync(
            profile,
            progress,
            cancellationToken);
        if (configUpdate.ErrorMessage is not null)
            return new SplitRoutingResultDto(false, 0, null, configUpdate.ErrorMessage);

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
