using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Application.Handlers;

public sealed class DisconnectProfileHandler(
    IProfileStore profileStore,
    IWireGuardBackendFactory backendFactory,
    ILogger<DisconnectProfileHandler> logger)
{
    public async Task<OperationResultDto> HandleAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new OperationResultDto(false, "Профиль не найден");

        try
        {
            logger.LogInformation("Отключение профиля {Name} ({Backend})", profile.Name, profile.Backend);
            var backend = backendFactory.GetBackend(profile.Backend);
            await backend.DisconnectAsync(profile, cancellationToken);
            logger.LogInformation("Профиль {Name} отключён", profile.Name);
            return new OperationResultDto(true, null);
        }
        catch (WireGuardOperationException ex)
        {
            logger.LogWarning("Отключение {Name} не удалось: {Message}", profile.Name, ex.UserMessage);
            return new OperationResultDto(false, ex.UserMessage);
        }
    }
}
