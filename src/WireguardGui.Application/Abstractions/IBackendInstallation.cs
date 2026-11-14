using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IDistroProbe
{
    Task<DistroFamily> DetectAsync(CancellationToken cancellationToken = default);
}

public interface IBackendInstaller
{
    BackendInstallPlanDto? GetPlan(DistroFamily family);

    Task<BackendInstallResultDto> InstallAsync(
        BackendInstallPlanDto plan,
        IProgress<BackendInstallProgressDto>? progress,
        CancellationToken cancellationToken = default);
}

public interface IBackendVersionProbe
{
    Task<BackendVersionDto> ProbeAsync(CancellationToken cancellationToken = default);
}
