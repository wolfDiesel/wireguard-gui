using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class GetProfilesHandler(
    IProfileStore profileStore,
    IWireGuardBackendFactory backendFactory)
{
    public async Task<IReadOnlyList<ProfileSummaryDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = await profileStore.ListProfilesAsync(cancellationToken);
        var result = new List<ProfileSummaryDto>(profiles.Count);

        foreach (var profile in profiles)
        {
            ConnectionState state;
            try
            {
                var backend = backendFactory.GetBackend(profile.Backend);
                state = await backend.GetConnectionStateAsync(profile, cancellationToken);
            }
            catch
            {
                state = ConnectionState.Unknown;
            }

            result.Add(new ProfileSummaryDto(
                profile.Id,
                profile.Name,
                profile.ConnectionName,
                profile.Backend,
                state,
                profile.SplitRouting.Enabled));
        }

        return result;
    }
}
