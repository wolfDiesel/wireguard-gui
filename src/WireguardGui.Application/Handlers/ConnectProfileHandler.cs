using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class ConnectProfileHandler(
    IProfileStore profileStore,
    IWireGuardBackendFactory backendFactory,
    ISplitRoutingConfigUpdater splitRoutingConfigUpdater,
    ILogger<ConnectProfileHandler> logger)
{
    public async Task<OperationResultDto> HandleAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new OperationResultDto(false, "Profile not found");

        var backend = backendFactory.GetBackend(profile.Backend);

        SplitRoutingConfigUpdateResult? configUpdate = null;
        if (profile.SplitRouting.Enabled)
        {
            logger.LogInformation("Split routing enabled for {Name}, scanning routes…", profile.Name);
            configUpdate = await splitRoutingConfigUpdater.TryUpdateConfigAsync(
                profile,
                cancellationToken: cancellationToken);
            if (configUpdate.ErrorMessage is not null)
                return new OperationResultDto(false, configUpdate.ErrorMessage);
        }

        try
        {
            logger.LogInformation("Connecting profile {Name} ({Backend})", profile.Name, profile.Backend);

            if (profile.SplitRouting.Enabled)
                await backend.ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);
            else
                await backend.ConnectAsync(profile, cancellationToken);

            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            if (state != ConnectionState.Connected)
                return new OperationResultDto(false, "Connection not active after connect");

            logger.LogInformation("Profile {Name} connected", profile.Name);
            return new OperationResultDto(true, null);
        }
        catch (WireGuardOperationException ex)
        {
            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            if (state == ConnectionState.Connected)
            {
                logger.LogWarning(
                    "Connect {Name}: error \"{Message}\", but connection is active",
                    profile.Name,
                    ex.UserMessage);
                return new OperationResultDto(true, null);
            }

            logger.LogWarning("Connect {Name} failed: {Message}", profile.Name, ex.UserMessage);
            return new OperationResultDto(false, ex.UserMessage);
        }
    }
}
