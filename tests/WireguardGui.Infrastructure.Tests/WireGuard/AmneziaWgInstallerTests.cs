using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.WireGuard;

public class AmneziaWgInstallerTests
{
    private readonly AmneziaWgInstaller _installer = new(new RecordingRunner());

    [Theory]
    [InlineData(DistroFamily.Debian)]
    [InlineData(DistroFamily.Fedora)]
    [InlineData(DistroFamily.Rhel)]
    [InlineData(DistroFamily.Alt)]
    public void GetPlan_ReturnsSteps_ForSupportedDistro(DistroFamily family)
    {
        var plan = _installer.GetPlan(family);

        Assert.NotNull(plan);
        Assert.NotEmpty(plan!.Steps);
        Assert.Contains(plan.Steps, static step => step.Script.Contains("amneziawg", StringComparison.Ordinal));
    }

    [Fact]
    public void GetPlan_ReturnsNull_ForUnknownDistro() =>
        Assert.Null(_installer.GetPlan(DistroFamily.Unknown));

    [Fact]
    public void GetPlan_Fedora_BuildsFromSource()
    {
        var plan = _installer.GetPlan(DistroFamily.Fedora)!;

        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("golang", StringComparison.Ordinal));
        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("amneziawg-tools.git", StringComparison.Ordinal));
        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("amneziawg-go", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Steps,
            static step => step.Script.Contains("copr enable", StringComparison.Ordinal));
        Assert.All(
            plan.Steps.Take(plan.Steps.Count - 1),
            static step => Assert.True(step.BestEffort));
        Assert.False(plan.Steps[^1].BestEffort);
    }

    [Fact]
    public async Task Install_Fedora_ContinuesPastFailedBuild_AndReportsVerifyFailure()
    {
        var runner = new RecordingRunner
        {
            FailingScripts =
            [
                "cd /tmp/amneziawg-build/tools/src && make && make install",
                "command -v awg && command -v awg-quick && command -v amneziawg-go",
            ],
        };
        var installer = new AmneziaWgInstaller(runner);
        var plan = installer.GetPlan(DistroFamily.Fedora)!;

        var result = await installer.InstallAsync(plan, progress: null);

        Assert.False(result.Success);
        Assert.Contains("Verify awg tools", result.ErrorMessage);
        Assert.Equal(plan.Steps.Count + 1, runner.Scripts.Count);
    }

    [Fact]
    public void GetPlan_Rhel_UsesCoprRepo()
    {
        var plan = _installer.GetPlan(DistroFamily.Rhel)!;

        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("copr enable amneziavpn/amneziawg", StringComparison.Ordinal));
        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("amneziawg-dkms", StringComparison.Ordinal));
        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("epel-release", StringComparison.Ordinal) && step.BestEffort);
    }

    [Fact]
    public void GetPlan_Debian_UsesPpaAndBestEffortTools()
    {
        var plan = _installer.GetPlan(DistroFamily.Debian)!;

        Assert.Contains(
            plan.Steps,
            static step => step.Script.Contains("ppa:amnezia/ppa", StringComparison.Ordinal));
        var toolsStep = plan.Steps.Single(step => step.Script.Contains("amneziawg-tools", StringComparison.Ordinal));
        Assert.True(toolsStep.BestEffort);
    }

    [Fact]
    public void GetPlan_Alt_UsesAltRepositories()
    {
        var plan = _installer.GetPlan(DistroFamily.Alt)!;

        Assert.Contains(
            plan.Steps,
            static step => step.Script == "apt-get install -y amneziawg amneziawg-tools");
    }

    [Fact]
    public async Task Install_WakesSessionAndRunsEveryStep()
    {
        var runner = new RecordingRunner();
        var installer = new AmneziaWgInstaller(runner);
        var plan = installer.GetPlan(DistroFamily.Alt)!;

        var result = await installer.InstallAsync(plan, progress: null);

        Assert.True(result.Success);
        Assert.Equal("true", runner.Scripts[0]);
        Assert.Equal(plan.Steps.Select(static step => step.Script), runner.Scripts.Skip(1));
    }

    [Fact]
    public async Task Install_StopsOnFailedStep()
    {
        var runner = new RecordingRunner { FailingScripts = ["apt-get update"] };
        var installer = new AmneziaWgInstaller(runner);
        var plan = installer.GetPlan(DistroFamily.Alt)!;

        var result = await installer.InstallAsync(plan, progress: null);

        Assert.False(result.Success);
        Assert.Contains("apt-get update", result.ErrorMessage);
        Assert.Equal(["true", "apt-get update"], runner.Scripts);
    }

    [Fact]
    public async Task Install_ContinuesAfterBestEffortFailure()
    {
        var runner = new RecordingRunner { FailingScripts = ["apt-get install -y amneziawg-tools"] };
        var installer = new AmneziaWgInstaller(runner);
        var plan = installer.GetPlan(DistroFamily.Debian)!;

        var result = await installer.InstallAsync(plan, progress: null);

        Assert.True(result.Success);
        Assert.Equal(plan.Steps.Count + 1, runner.Scripts.Count);
    }

    [Fact]
    public async Task Install_ReportsProgress()
    {
        var installer = new AmneziaWgInstaller(new RecordingRunner());
        var plan = installer.GetPlan(DistroFamily.Fedora)!;
        var progress = new RecordingProgress();

        await installer.InstallAsync(plan, progress);

        Assert.Contains(progress.Items, static item => item.StepIndex == 0);
        Assert.Equal(plan.Steps.Count, progress.Items.Max(static item => item.StepIndex));
        Assert.All(progress.Items, item => Assert.Equal(plan.Steps.Count, item.StepCount));
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        public HashSet<string> FailingScripts { get; init; } = new(StringComparer.Ordinal);

        public List<string> Scripts { get; } = [];

        public bool HasActivePrivilegedSession => false;

        public bool IsCommandAvailable(string command) => true;

        public Task<ProcessResult> RunPrivilegedShellAsync(
            string script,
            CancellationToken cancellationToken = default)
        {
            Scripts.Add(script);
            return Task.FromResult(FailingScripts.Contains(script)
                ? new ProcessResult(1, string.Empty, $"error in {script}")
                : new ProcessResult(0, $"ok: {script}", string.Empty));
        }

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public Task<ProcessResult> RunPrivilegedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, cancellationToken);
    }

    private sealed class RecordingProgress : IProgress<BackendInstallProgressDto>
    {
        public List<BackendInstallProgressDto> Items { get; } = [];

        public void Report(BackendInstallProgressDto value) => Items.Add(value);
    }
}
