using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IProfileImporter
{
    Task<ImportProfileResultDto> ImportFromFileAsync(
        string sourceConfigPath,
        BackendKind backend,
        CancellationToken cancellationToken = default);
}
