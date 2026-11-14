using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class AmneziaWgInstaller(IProcessRunner processRunner) : IBackendInstaller
{
    private const string WakeSessionScript = "true";
    private const string VerifyToolsScript = "command -v awg && command -v awg-quick";
    private const string VerifyUserspaceScript = "command -v awg && command -v awg-quick && command -v amneziawg-go";

    private static readonly BackendInstallPlanDto DebianPlan = new(
        DistroFamily.Debian,
        [
            new("apt-get update", "apt-get update"),
            new(
                "apt-get install -y software-properties-common gnupg2",
                "apt-get install -y software-properties-common gnupg2"),
            new("add-apt-repository -y ppa:amnezia/ppa", "add-apt-repository -y ppa:amnezia/ppa"),
            new("apt-get update", "apt-get update"),
            new("apt-get install -y amneziawg", "apt-get install -y amneziawg"),
            new(
                "apt-get install -y amneziawg-tools",
                "apt-get install -y amneziawg-tools",
                BestEffort: true),
            new("Verify awg tools", VerifyToolsScript),
        ]);

    private static readonly BackendInstallPlanDto FedoraPlan = new(
        DistroFamily.Fedora,
        [
            new("dnf -y install git make gcc golang", "dnf -y install git make gcc golang", BestEffort: true),
            new(
                "Clone amneziawg-tools",
                "rm -rf /tmp/amneziawg-build && mkdir -p /tmp/amneziawg-build && "
                + "git clone --depth 1 https://github.com/amnezia-vpn/amneziawg-tools.git "
                + "/tmp/amneziawg-build/tools",
                BestEffort: true),
            new(
                "Build and install awg tools",
                "cd /tmp/amneziawg-build/tools/src && make && make install",
                BestEffort: true),
            new(
                "Clone amneziawg-go",
                "git clone --depth 1 https://github.com/amnezia-vpn/amneziawg-go.git /tmp/amneziawg-build/go",
                BestEffort: true),
            new(
                "Build and install amneziawg-go",
                "cd /tmp/amneziawg-build/go && make && install -m 0755 amneziawg-go /usr/bin/amneziawg-go",
                BestEffort: true),
            new("Verify awg tools", VerifyUserspaceScript),
        ]);

    private static readonly BackendInstallPlanDto RhelPlan = new(
        DistroFamily.Rhel,
        [
            new("dnf -y install dnf-plugins-core", "dnf -y install dnf-plugins-core"),
            new("dnf -y install epel-release", "dnf -y install epel-release", BestEffort: true),
            new(
                "dnf -y copr enable amneziavpn/amneziawg",
                "dnf -y copr enable amneziavpn/amneziawg"),
            new(
                "dnf -y install amneziawg-dkms amneziawg-tools",
                "dnf -y install amneziawg-dkms amneziawg-tools"),
            new("Verify awg tools", VerifyToolsScript),
        ]);

    private static readonly BackendInstallPlanDto AltPlan = new(
        DistroFamily.Alt,
        [
            new("apt-get update", "apt-get update"),
            new(
                "apt-get install -y amneziawg amneziawg-tools",
                "apt-get install -y amneziawg amneziawg-tools"),
            new("Verify awg tools", VerifyToolsScript),
        ]);

    public BackendInstallPlanDto? GetPlan(DistroFamily family) =>
        family switch
        {
            DistroFamily.Debian => DebianPlan,
            DistroFamily.Fedora => FedoraPlan,
            DistroFamily.Rhel => RhelPlan,
            DistroFamily.Alt => AltPlan,
            _ => null,
        };

    public async Task<BackendInstallResultDto> InstallAsync(
        BackendInstallPlanDto plan,
        IProgress<BackendInstallProgressDto>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new BackendInstallProgressDto(
            0,
            plan.Steps.Count,
            "Requesting administrator rights",
            null));

        await processRunner.RunPrivilegedShellAsync(WakeSessionScript, cancellationToken)
            .ConfigureAwait(false);

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            progress?.Report(new BackendInstallProgressDto(index + 1, plan.Steps.Count, step.Title, null));

            var result = await processRunner.RunPrivilegedShellAsync(step.Script, cancellationToken)
                .ConfigureAwait(false);
            var output = Tail(result);

            progress?.Report(new BackendInstallProgressDto(
                index + 1,
                plan.Steps.Count,
                step.Title,
                output));

            if (!result.IsSuccess && !step.BestEffort)
                return new BackendInstallResultDto(
                    false,
                    $"Step failed: {step.Title} (exit {result.ExitCode})",
                    output);
        }

        return new BackendInstallResultDto(true);
    }

    private static string? Tail(ProcessResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        var lines = text.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Length == 0 ? null : string.Join('\n', lines.TakeLast(5));
    }
}
