using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.WireGuard;

public class AmneziaWgVersionProbeTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "wg-awg-version-" + Guid.NewGuid().ToString("N"));

    public AmneziaWgVersionProbeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ProbeAsync_ReadsToolsVersion()
    {
        var runner = new VersionRunner
        {
            AvailableCommands = ["awg", "modinfo"],
            AwgVersion = new ProcessResult(0, "amneziawg-tools v1.0.20240925\n", string.Empty),
        };
        var probe = new AmneziaWgVersionProbe(runner, MissingModulePath());

        var version = await probe.ProbeAsync();

        Assert.Equal("amneziawg-tools v1.0.20240925", version.ToolsVersion);
        Assert.Equal(AmneziaWgTransport.None, version.Transport);
        Assert.Null(version.TransportVersion);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsNullToolsVersion_OnFailure()
    {
        var runner = new VersionRunner
        {
            AvailableCommands = ["awg"],
            AwgVersion = new ProcessResult(1, string.Empty, "boom"),
        };
        var probe = new AmneziaWgVersionProbe(runner, MissingModulePath());

        Assert.Null((await probe.ProbeAsync()).ToolsVersion);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsNullToolsVersion_WithoutAwg()
    {
        var probe = new AmneziaWgVersionProbe(new VersionRunner(), MissingModulePath());

        Assert.Null((await probe.ProbeAsync()).ToolsVersion);
    }

    [Fact]
    public async Task ProbeAsync_DetectsKernelModule()
    {
        var modulePath = Path.Combine(_tempDir, "amneziawg");
        Directory.CreateDirectory(modulePath);
        var runner = new VersionRunner
        {
            AvailableCommands = ["awg", "modinfo"],
            AwgVersion = new ProcessResult(0, "amneziawg-tools v1.0\n", string.Empty),
            ModinfoVersion = new ProcessResult(0, "1.0.20240925\n", string.Empty),
        };
        var probe = new AmneziaWgVersionProbe(runner, modulePath);

        var version = await probe.ProbeAsync();

        Assert.Equal(AmneziaWgTransport.KernelModule, version.Transport);
        Assert.Equal("1.0.20240925", version.TransportVersion);
    }

    [Fact]
    public async Task ProbeAsync_DetectsUserspaceTransport()
    {
        var runner = new VersionRunner { AvailableCommands = ["amneziawg-go"] };
        var probe = new AmneziaWgVersionProbe(runner, MissingModulePath());

        var version = await probe.ProbeAsync();

        Assert.Equal(AmneziaWgTransport.Userspace, version.Transport);
        Assert.Null(version.TransportVersion);
    }

    private string MissingModulePath() => Path.Combine(_tempDir, "amneziawg-absent");

    private sealed class VersionRunner : IProcessRunner
    {
        public IReadOnlyList<string> AvailableCommands { get; init; } = [];

        public ProcessResult AwgVersion { get; init; } = new(1, string.Empty, "unavailable");

        public ProcessResult ModinfoVersion { get; init; } = new(1, string.Empty, "unavailable");

        public bool HasActivePrivilegedSession => false;

        public bool IsCommandAvailable(string command) => AvailableCommands.Contains(command);

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(fileName == "awg" ? AwgVersion : ModinfoVersion);

        public Task<ProcessResult> RunPrivilegedAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, cancellationToken);

        public Task<ProcessResult> RunPrivilegedShellAsync(
            string script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}
