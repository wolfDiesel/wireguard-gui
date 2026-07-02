using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;

namespace WireguardGui.Application.Handlers;

public sealed class DeleteProfileHandler(
    IProfileStore profileStore,
    IWireGuardBackendFactory backendFactory,
    ILogger<DeleteProfileHandler> logger)
{
    public async Task<OperationResultDto> HandleAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new OperationResultDto(false, OperationErrorCode.ProfileNotFound, "Profile not found");

        var backend = backendFactory.GetBackend(profile.Backend);
        string? warning = null;

        try
        {
            logger.LogInformation("Deleting profile {Name} ({Backend})", profile.Name, profile.Backend);
            await backend.DisconnectAsync(profile, cancellationToken);
            await backend.UnregisterAsync(profile, cancellationToken);
        }
        catch (Exceptions.WireGuardOperationException ex)
        {
            logger.LogWarning(ex, "Error disconnecting/unregistering {Name} from system", profile.Name);
            warning = ex.UserMessage;
        }

        await profileStore.DeleteProfileAsync(profileId, cancellationToken);
        logger.LogInformation("Profile {Name} deleted", profile.Name);
        return new OperationResultDto(true, OperationErrorCode.None, null, warning);
    }
}
