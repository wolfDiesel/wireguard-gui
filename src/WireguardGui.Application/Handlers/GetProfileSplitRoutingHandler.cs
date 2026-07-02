using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class GetProfileSplitRoutingHandler(IProfileStore profileStore)
{
    public async Task<SplitRoutingSettingsResultDto> HandleAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileStore.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
            return new SplitRoutingSettingsResultDto(false, null, "Profile not found");

        return new SplitRoutingSettingsResultDto(true, profile.SplitRouting, null);
    }
}
