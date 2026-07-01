using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;

namespace WireguardGui.Application.Handlers;

public sealed class GetSystemCapabilitiesHandler(ISystemCapabilityProbe probe)
{
    public async Task<SystemCapabilitiesDto> HandleAsync(CancellationToken cancellationToken = default)
    {
        var backends = await probe.ProbeAllAsync(cancellationToken);
        var dtos = backends.Select(b => new BackendCapabilityDto(
            b.Backend,
            b.IsAvailable,
            b.MissingComponents,
            b.FedoraInstallHint,
            b.DebianInstallHint)).ToList();

        return new SystemCapabilitiesDto(dtos, dtos.Any(b => b.IsAvailable));
    }
}
