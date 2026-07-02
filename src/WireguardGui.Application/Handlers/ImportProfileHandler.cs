using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;

namespace WireguardGui.Application.Handlers;

public sealed class ImportProfileHandler(
    ISystemCapabilityProbe capabilityProbe,
    IProfileImporter profileImporter)
{
    public async Task<ImportProfileResultDto> HandleAsync(
        ImportProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var capabilities = await capabilityProbe.ProbeAllAsync(cancellationToken);
        var backendCap = capabilities.FirstOrDefault(c => c.Backend == request.Backend);
        if (backendCap is null || !backendCap.IsAvailable)
            return new ImportProfileResultDto(false, null, $"{request.Backend} backend is unavailable");

        return await profileImporter.ImportFromFileAsync(
            request.SourceConfigPath,
            request.Backend,
            cancellationToken);
    }
}
