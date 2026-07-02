using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.System;

public sealed class SystemCapabilityProbe(IProcessRunner processRunner) : ISystemCapabilityProbe
{
    public Task<BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ProbeNative());

    public async Task<BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default) =>
        await ProbeNmcliAsyncCore(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default)
    {
        var nmcli = await ProbeNmcliAsyncCore(cancellationToken).ConfigureAwait(false);
        return [ProbeNative(), nmcli];
    }

    private BackendCapability ProbeNative()
    {
        var missing = new List<string>();

        if (!processRunner.IsCommandAvailable("wg"))
            missing.Add("wg");
        if (!processRunner.IsCommandAvailable("wg-quick"))
            missing.Add("wg-quick");
        if (!IsWireGuardModuleAvailable())
            missing.Add("wireguard kernel module");

        return new BackendCapability(
            BackendKind.Native,
            missing.Count == 0,
            missing,
            "sudo dnf install wireguard-tools",
            "sudo apt install wireguard");
    }

    private async Task<BackendCapability> ProbeNmcliAsyncCore(CancellationToken cancellationToken)
    {
        var missing = new List<string>();

        if (!processRunner.IsCommandAvailable("nmcli"))
            missing.Add("nmcli");
        else if (!await IsNetworkManagerActiveAsync(cancellationToken).ConfigureAwait(false))
            missing.Add("NetworkManager (not active)");

        return new BackendCapability(
            BackendKind.Nmcli,
            missing.Count == 0,
            missing,
            "sudo dnf install NetworkManager-wireguard",
            "sudo apt install network-manager");
    }

    private static bool IsWireGuardModuleAvailable() =>
        Directory.Exists("/sys/module/wireguard");

    private async Task<bool> IsNetworkManagerActiveAsync(CancellationToken cancellationToken)
    {
        if (!processRunner.IsCommandAvailable("nmcli"))
            return false;

        try
        {
            var result = await processRunner.RunAsync(
                "nmcli",
                ["-t", "-f", "RUNNING", "general"],
                cancellationToken).ConfigureAwait(false);
            return result.StandardOutput.Trim().Equals("running", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
