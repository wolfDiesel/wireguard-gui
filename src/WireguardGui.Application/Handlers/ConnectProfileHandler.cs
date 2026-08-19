using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;
using WireguardGui.Application.Services;

namespace WireguardGui.Application.Handlers;

public sealed class ConnectProfileHandler(
    IProfileStore profileStore,
    IWireGuardBackendFactory backendFactory,
    ISplitRoutingConfigUpdater splitRoutingConfigUpdater,
    IPolicyRoutingSetup policyRoutingSetup,
    ILogger<ConnectProfileHandler> logger)
{
    public async Task<OperationResultDto> HandleAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new OperationResultDto(false, OperationErrorCode.ProfileNotFound, "Profile not found");

        var backend = backendFactory.GetBackend(profile.Backend);

        SplitRoutingConfigUpdateResult? configUpdate = null;
        if (profile.SplitRouting.Enabled)
        {
            logger.LogInformation("Split routing enabled for {Name}, scanning routes…", profile.Name);
            configUpdate = await splitRoutingConfigUpdater.TryUpdateConfigAsync(
                profile,
                cancellationToken: cancellationToken);
            if (configUpdate.ErrorMessage is not null)
                return new OperationResultDto(false, OperationErrorCode.NoRoutesGenerated, configUpdate.ErrorMessage);
        }

        try
        {
            logger.LogInformation("Connecting profile {Name} ({Backend})", profile.Name, profile.Backend);

            if (configUpdate?.UsesPolicyRouting == true)
                await policyRoutingSetup.PrepareConnectionAsync(profile, cancellationToken);

            if (profile.SplitRouting.Enabled && configUpdate?.Changed == true)
                await backend.ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);
            else
                await backend.ConnectAsync(profile, cancellationToken);

            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            if (state != ConnectionState.Connected)
                return new OperationResultDto(false, OperationErrorCode.ConnectionFailed, "Connection not active after connect");

            if (configUpdate?.UsesPolicyRouting == true &&
                configUpdate.Routes is { Count: > 0 })
            {
                var policyResult = await policyRoutingSetup.ApplyAsync(
                    profile,
                    configUpdate.Routes,
                    cancellationToken);
                if (!policyResult.Success)
                {
                    logger.LogWarning(
                        "Policy routing setup failed for {Name}: {Message}",
                        profile.Name,
                        policyResult.ErrorMessage);
                    return new OperationResultDto(
                        true,
                        WarningMessage: policyResult.ErrorMessage ?? "Policy routing setup failed");
                }
            }

            logger.LogInformation("Profile {Name} connected", profile.Name);
            return new OperationResultDto(true);
        }
        catch (WireGuardOperationException ex)
        {
            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            return ConnectionOutcomeResolver.ResolveAfterFailure(state, ex.UserMessage);
        }
    }
}
