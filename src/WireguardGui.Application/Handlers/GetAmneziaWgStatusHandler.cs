using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;

namespace WireguardGui.Application.Handlers;

public sealed class GetAmneziaWgStatusHandler(
    ISystemCapabilityProbe capabilityProbe,
    IBackendVersionProbe versionProbe,
    IDistroProbe distroProbe)
{
    public async Task<AmneziaWgStatusDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var capability = await capabilityProbe.ProbeAmneziaWgAsync(cancellationToken);
        var version = await versionProbe.ProbeAsync(cancellationToken);
        var distro = await distroProbe.DetectAsync(cancellationToken);

        return new AmneziaWgStatusDto(
            capability.IsAvailable,
            capability.MissingComponents,
            version.ToolsVersion,
            version.Transport,
            version.TransportVersion,
            distro);
    }
}
