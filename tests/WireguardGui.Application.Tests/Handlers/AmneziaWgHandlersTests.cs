using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;

namespace WireguardGui.Application.Tests.Handlers;

public class AmneziaWgHandlersTests
{
    [Fact]
    public async Task GetStatus_MapsCapabilityVersionAndDistro()
    {
        var handler = new GetAmneziaWgStatusHandler(
            new CapabilityProbe { Available = false },
            new VersionProbe
            {
                Version = new BackendVersionDto("amneziawg-tools v1.0", AmneziaWgTransport.KernelModule, "1.0.1"),
            },
            new DistroProbe(DistroFamily.Fedora));

        var status = await handler.HandleAsync();

        Assert.False(status.IsAvailable);
        Assert.Contains("awg", status.MissingComponents);
        Assert.Equal("amneziawg-tools v1.0", status.ToolsVersion);
        Assert.Equal(AmneziaWgTransport.KernelModule, status.Transport);
        Assert.Equal("1.0.1", status.TransportVersion);
        Assert.Equal(DistroFamily.Fedora, status.Distro);
    }

    [Fact]
    public async Task Install_SkipsInstaller_WhenBackendAvailable()
    {
        var installer = new Installer();
        var handler = new InstallAmneziaWgHandler(
            installer,
            new CapabilityProbe { Available = true },
            new DistroProbe(DistroFamily.Debian));

        var result = await handler.HandleAsync();

        Assert.True(result.Success);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Install_Fails_WhenDistroIsUnsupported()
    {
        var installer = new Installer { Plan = null };
        var handler = new InstallAmneziaWgHandler(
            installer,
            new CapabilityProbe { Available = false },
            new DistroProbe(DistroFamily.Unknown));

        var result = await handler.HandleAsync();

        Assert.False(result.Success);
        Assert.Contains("not supported", result.ErrorMessage);
        Assert.Equal(0, installer.InstallCalls);
    }

    [Fact]
    public async Task Install_ReportsNeedsAdmin_WhenPrivilegesRequired()
    {
        var installer = new Installer { Failure = new PrivilegeRequiredException() };
        var handler = new InstallAmneziaWgHandler(
            installer,
            new CapabilityProbe { Available = false },
            new DistroProbe(DistroFamily.Debian));

        var result = await handler.HandleAsync();

        Assert.False(result.Success);
        Assert.True(result.NeedsAdmin);
        Assert.Contains("privileges", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Install_PassesProgressAndPlanToInstaller()
    {
        var installer = new Installer { Plan = SamplePlan };
        var handler = new InstallAmneziaWgHandler(
            installer,
            new CapabilityProbe { Available = false },
            new DistroProbe(DistroFamily.Alt));
        var progress = new RecordingProgress();

        var result = await handler.HandleAsync(progress);

        Assert.True(result.Success);
        Assert.Equal(1, installer.InstallCalls);
        Assert.Same(progress, installer.LastProgress);
        Assert.Equal(SamplePlan, installer.LastPlan);
    }

    private static readonly BackendInstallPlanDto SamplePlan = new(
        DistroFamily.Alt,
        [new BackendInstallStepDto("apt-get update", "apt-get update")]);

    private sealed class RecordingProgress : IProgress<BackendInstallProgressDto>
    {
        public void Report(BackendInstallProgressDto value)
        {
        }
    }

    private sealed class CapabilityProbe : ISystemCapabilityProbe
    {
        public bool Available { get; init; }

        public Task<BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable(BackendKind.Native));

        public Task<BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable(BackendKind.Nmcli));

        public Task<BackendCapability> ProbeAmneziaWgAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BackendCapability(
                BackendKind.AmneziaWg,
                Available,
                Available ? [] : ["awg", "awg-quick"],
                "sudo dnf copr enable amneziavpn/amneziawg",
                "sudo add-apt-repository ppa:amnezia/ppa"));

        public Task<IReadOnlyList<BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackendCapability>>([Unavailable(BackendKind.Native)]);

        public SplitRoutingToolingCapability ProbeSplitRoutingTooling() =>
            new(true, true, true, [], string.Empty, string.Empty);

        private static BackendCapability Unavailable(BackendKind backend) =>
            new(backend, false, [backend.ToString()], string.Empty, string.Empty);
    }

    private sealed class VersionProbe : IBackendVersionProbe
    {
        public BackendVersionDto Version { get; init; } =
            new(null, AmneziaWgTransport.None, null);

        public Task<BackendVersionDto> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Version);
    }

    private sealed class DistroProbe(DistroFamily family) : IDistroProbe
    {
        public Task<DistroFamily> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(family);
    }

    private sealed class Installer : IBackendInstaller
    {
        public BackendInstallPlanDto? Plan { get; init; } = new(
            DistroFamily.Debian,
            [new BackendInstallStepDto("apt-get update", "apt-get update")]);

        public BackendInstallResultDto Result { get; init; } = new(true);

        public Exception? Failure { get; init; }

        public int InstallCalls { get; private set; }

        public IProgress<BackendInstallProgressDto>? LastProgress { get; private set; }

        public BackendInstallPlanDto? LastPlan { get; private set; }

        public BackendInstallPlanDto? GetPlan(DistroFamily family) => Plan;

        public Task<BackendInstallResultDto> InstallAsync(
            BackendInstallPlanDto plan,
            IProgress<BackendInstallProgressDto>? progress,
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            LastPlan = plan;
            LastProgress = progress;

            if (Failure is not null)
                throw Failure;

            return Task.FromResult(Result);
        }
    }
}
