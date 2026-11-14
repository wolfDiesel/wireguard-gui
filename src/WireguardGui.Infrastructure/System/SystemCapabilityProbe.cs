using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.System;

public class SystemCapabilityProbe(IProcessRunner processRunner) : ISystemCapabilityProbe
{
    public Task<BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ProbeNative());

    public async Task<BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default) =>
        await ProbeNmcliAsyncCore(cancellationToken).ConfigureAwait(false);

    public Task<BackendCapability> ProbeAmneziaWgAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ProbeAmneziaWg());

    public async Task<IReadOnlyList<BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default)
    {
        var nmcli = await ProbeNmcliAsyncCore(cancellationToken).ConfigureAwait(false);
        return [ProbeNative(), nmcli, ProbeAmneziaWg()];
    }

    protected virtual bool IsKernelModulePresent(string moduleName) =>
        Directory.Exists(Path.Combine("/sys/module", moduleName));

    private BackendCapability ProbeNative()
    {
        var missing = new List<string>();

        if (!processRunner.IsCommandAvailable("wg"))
            missing.Add("wg");
        if (!processRunner.IsCommandAvailable("wg-quick"))
            missing.Add("wg-quick");
        if (!IsKernelModulePresent("wireguard"))
            missing.Add("wireguard kernel module");

        return new BackendCapability(
            BackendKind.Native,
            missing.Count == 0,
            missing,
            "sudo dnf install wireguard-tools",
            "sudo apt install wireguard");
    }

    private BackendCapability ProbeAmneziaWg()
    {
        var missing = new List<string>();

        if (!processRunner.IsCommandAvailable("awg"))
            missing.Add("awg");
        if (!processRunner.IsCommandAvailable("awg-quick"))
            missing.Add("awg-quick");
        if (!IsAmneziaWgTransportAvailable())
            missing.Add("amneziawg kernel module or amneziawg-go");

        return new BackendCapability(
            BackendKind.AmneziaWg,
            missing.Count == 0,
            missing,
            "sudo dnf copr enable amneziavpn/amneziawg && sudo dnf install amneziawg-tools amneziawg-dkms",
            "sudo add-apt-repository ppa:amnezia/ppa && sudo apt install amneziawg amneziawg-tools");
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

    private bool IsAmneziaWgTransportAvailable() =>
        IsKernelModulePresent("amneziawg") || processRunner.IsCommandAvailable("amneziawg-go");

    public SplitRoutingToolingCapability ProbeSplitRoutingTooling()
    {
        var missing = new List<string>();
        var fedora = new List<string>();
        var debian = new List<string>();

        var hasIp = processRunner.IsCommandAvailable("ip");
        if (!hasIp)
        {
            missing.Add("ip");
            fedora.Add("iproute");
            debian.Add("iproute2");
        }

        var hasDig = processRunner.IsCommandAvailable("dig");
        if (!hasDig)
        {
            missing.Add("dig");
            fedora.Add("bind-utils");
            debian.Add("dnsutils");
        }

        var hasResolvectl = processRunner.IsCommandAvailable("resolvectl");
        if (!hasResolvectl)
        {
            missing.Add("resolvectl");
            fedora.Add("systemd");
            debian.Add("systemd");
        }

        return new SplitRoutingToolingCapability(
            hasIp,
            hasDig,
            hasResolvectl,
            missing,
            fedora.Count == 0 ? string.Empty : "sudo dnf install " + string.Join(' ', fedora.Distinct()),
            debian.Count == 0 ? string.Empty : "sudo apt install " + string.Join(' ', debian.Distinct()));
    }

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
