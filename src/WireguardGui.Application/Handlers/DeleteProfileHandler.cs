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
            return new OperationResultDto(false, "Профиль не найден");

        var backend = backendFactory.GetBackend(profile.Backend);

        try
        {
            logger.LogInformation("Удаление профиля {Name} ({Backend})", profile.Name, profile.Backend);
            await backend.DisconnectAsync(profile, cancellationToken);
            await backend.UnregisterAsync(profile, cancellationToken);
        }
        catch (Exceptions.WireGuardOperationException ex)
        {
            logger.LogWarning(ex, "Ошибка при отключении/удалении {Name} из системы", profile.Name);
            return new OperationResultDto(false, ex.UserMessage);
        }

        await profileStore.DeleteProfileAsync(profileId, cancellationToken);
        logger.LogInformation("Профиль {Name} удалён", profile.Name);
        return new OperationResultDto(true, null);
    }
}
