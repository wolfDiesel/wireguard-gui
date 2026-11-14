using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IConfigKindProbe
{
    Task<ConfigKind> DetectAsync(string configPath, CancellationToken cancellationToken = default);
}
