using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class SaveProfileSplitRoutingHandler(IProfileStore profileStore)
{
    public async Task<OperationResultDto> HandleAsync(
        string profileId,
        SplitRoutingSettings settings,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new OperationResultDto(false, OperationErrorCode.ProfileNotFound, "Profile not found");

        var updated = profile with { SplitRouting = settings.Normalize() };
        await profileStore.SaveProfileAsync(updated, cancellationToken);
        return new OperationResultDto(true);
    }
}

public sealed class GetSettingsHandler(ISettingsStore settingsStore)
{
    public Task<AppSettings> HandleAsync(CancellationToken cancellationToken = default) =>
        settingsStore.LoadAsync(cancellationToken);
}

public sealed class SaveSettingsHandler(ISettingsStore settingsStore)
{
    public Task HandleAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        settingsStore.SaveAsync(settings, cancellationToken);
}
