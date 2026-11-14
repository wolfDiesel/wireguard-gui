using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Application.Handlers;

public sealed class InstallAmneziaWgHandler(
    IBackendInstaller installer,
    ISystemCapabilityProbe capabilityProbe,
    IDistroProbe distroProbe)
{
    public async Task<BackendInstallResultDto> HandleAsync(
        IProgress<BackendInstallProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var capability = await capabilityProbe.ProbeAmneziaWgAsync(cancellationToken);
        if (capability.IsAvailable)
            return new BackendInstallResultDto(true);

        var distro = await distroProbe.DetectAsync(cancellationToken);
        var plan = installer.GetPlan(distro);
        if (plan is null)
            return new BackendInstallResultDto(
                false,
                "Automatic installation is not supported on this distribution");

        try
        {
            return await installer.InstallAsync(plan, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PrivilegeRequiredException ex)
        {
            return new BackendInstallResultDto(false, ex.UserMessage, null, NeedsAdmin: true);
        }
        catch (WireGuardOperationException ex)
        {
            return new BackendInstallResultDto(false, ex.UserMessage, ex.Details);
        }
    }
}
