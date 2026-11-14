using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class DetectConfigKindHandler(IConfigKindProbe configKindProbe)
{
    public Task<ConfigKind> HandleAsync(string configPath, CancellationToken cancellationToken = default) =>
        configKindProbe.DetectAsync(configPath, cancellationToken);
}
