using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class ApplySplitRoutingHandler(
    IProfileStore profileStore,
    ISplitRoutingConfigUpdater splitRoutingConfigUpdater,
    IWireGuardBackendFactory backendFactory,
    ILogger<ApplySplitRoutingHandler> logger)
{
    public async Task<SplitRoutingResultDto> HandleAsync(
        string profileId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new SplitRoutingResultDto(false, 0, null, "Профиль не найден");

        if (!profile.SplitRouting.Enabled)
            return new SplitRoutingResultDto(false, 0, null, "Split routing отключён");

        logger.LogInformation("Применение split routing для {Profile}", profile.Name);

        var backend = backendFactory.GetBackend(profile.Backend);
        var wasConnected = await backend.GetConnectionStateAsync(profile, cancellationToken) == ConnectionState.Connected;
        if (wasConnected)
            progress?.Report("Progress_Reconnect_Required");

        var configUpdate = await splitRoutingConfigUpdater.TryUpdateConfigAsync(
            profile,
            progress,
            cancellationToken);
        if (configUpdate.ErrorMessage is not null)
            return new SplitRoutingResultDto(false, 0, null, configUpdate.ErrorMessage);

        if (!configUpdate.Changed)
        {
            logger.LogInformation(
                "Split routing {Profile}: конфиг не изменился ({Count} маршрутов)",
                profile.Name,
                configUpdate.RouteCount);
            progress?.Report("Progress_Routes_Unchanged");
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, null, null);
        }

        if (!wasConnected)
        {
            progress?.Report($"Progress_Routes_Written|{configUpdate.RouteCount}");
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
        }

        progress?.Report("Progress_Reconnect_Nm");
        logger.LogInformation("Split routing {Profile}: переподключение после обновления маршрутов", profile.Name);

        try
        {
            await backend.ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);
            progress?.Report($"Progress_Done|{configUpdate.RouteCount}");
            return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
        }
        catch (WireGuardOperationException ex)
        {
            profile = await profileStore.GetProfileAsync(profileId, cancellationToken) ?? profile;
            var state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            if (state == ConnectionState.Connected)
            {
                progress?.Report($"Progress_Done|{configUpdate.RouteCount}");
                return new SplitRoutingResultDto(true, configUpdate.RouteCount, configUpdate.RoutesCsv, null);
            }

            return new SplitRoutingResultDto(false, configUpdate.RouteCount, configUpdate.RoutesCsv, ex.UserMessage);
        }
    }
}
