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
            return new OperationResultDto(false, "Профиль не найден");

        var backend = backendFactory.GetBackend(profile.Backend);

        SplitRoutingConfigUpdateResult? configUpdate = null;
        if (profile.SplitRouting.Enabled)
        {
            logger.LogInformation("Split routing включён для {Name}, сканирование маршрутов…", profile.Name);
            configUpdate = await splitRoutingConfigUpdater.TryUpdateConfigAsync(
                profile,
                cancellationToken: cancellationToken);
            if (configUpdate.ErrorMessage is not null)
                return new OperationResultDto(false, configUpdate.ErrorMessage);
        }

        try
        {
            logger.LogInformation("Подключение профиля {Name} ({Backend})", profile.Name, profile.Backend);

            if (profile.SplitRouting.Enabled)
                await backend.ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);
            else
                await backend.ConnectAsync(profile, cancellationToken);

            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            if (state != ConnectionState.Connected)
                return new OperationResultDto(false, "Соединение не активно после подключения");

            logger.LogInformation("Профиль {Name} подключён", profile.Name);
            return new OperationResultDto(true, null);
        }
        catch (WireGuardOperationException ex)
        {
            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            if (state == ConnectionState.Connected)
            {
                logger.LogWarning(
                    "Подключение {Name}: ошибка «{Message}», но соединение активно",
                    profile.Name,
                    ex.UserMessage);
                return new OperationResultDto(true, null);
            }

            logger.LogWarning("Подключение {Name} не удалось: {Message}", profile.Name, ex.UserMessage);
            return new OperationResultDto(false, ex.UserMessage);
        }
    }
}
