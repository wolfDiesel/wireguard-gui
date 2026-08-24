using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class SaveProfileSplitRoutingHandler(
    IProfileStore profileStore,
    IProfileConfigDnsSync profileConfigDnsSync)
{
    public async Task<OperationResultDto> HandleAsync(
        string profileId,
        SplitRoutingSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!TunnelDnsServers.TryParse(settings.TunnelDns, out var tunnelDns, out var dnsError))
            return new OperationResultDto(false, OperationErrorCode.ConfigInvalid, dnsError);

        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new OperationResultDto(false, OperationErrorCode.ProfileNotFound, "Profile not found");

        var normalized = settings with { TunnelDns = tunnelDns };
        var updated = profile with { SplitRouting = normalized.Normalize() };
        await profileStore.SaveProfileAsync(updated, cancellationToken);

        var syncResult = await profileConfigDnsSync.SyncTunnelDnsAsync(updated, cancellationToken)
            .ConfigureAwait(false);
        if (!syncResult.Success)
            return syncResult;

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
        settingsStore.SaveAsync(settings.Normalize(), cancellationToken);
}
