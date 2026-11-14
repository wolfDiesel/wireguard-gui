using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class AmneziaWgVersionProbe(
    IProcessRunner processRunner,
    string kernelModulePath = "/sys/module/amneziawg") : IBackendVersionProbe
{
    public async Task<BackendVersionDto> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var toolsVersion = await ProbeToolsVersionAsync(cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(kernelModulePath))
        {
            return new BackendVersionDto(
                toolsVersion,
                AmneziaWgTransport.KernelModule,
                await ProbeModuleVersionAsync(cancellationToken).ConfigureAwait(false));
        }

        if (processRunner.IsCommandAvailable("amneziawg-go"))
            return new BackendVersionDto(toolsVersion, AmneziaWgTransport.Userspace, null);

        return new BackendVersionDto(toolsVersion, AmneziaWgTransport.None, null);
    }

    private async Task<string?> ProbeToolsVersionAsync(CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("awg"))
            return null;

        var result = await processRunner.RunAsync("awg", ["--version"], cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return null;

        var line = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private async Task<string?> ProbeModuleVersionAsync(CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("modinfo"))
            return null;

        var result = await processRunner.RunAsync("modinfo", ["-F", "version", "amneziawg"], cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return null;

        var version = result.StandardOutput.Trim();
        return version.Length == 0 ? null : version;
    }
}
