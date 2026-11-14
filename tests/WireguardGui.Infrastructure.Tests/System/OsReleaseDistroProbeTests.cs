using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.System;

namespace WireguardGui.Infrastructure.Tests.System;

public class OsReleaseDistroProbeTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "wg-os-release-" + Guid.NewGuid().ToString("N"));

    public OsReleaseDistroProbeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n", DistroFamily.Debian)]
    [InlineData("ID=\"/linuxmint\"\nID_LIKE=\"ubuntu debian\"\n", DistroFamily.Debian)]
    [InlineData("ID=fedora\n", DistroFamily.Fedora)]
    [InlineData("ID=\"rhel\"\nID_LIKE=\"fedora\"\n", DistroFamily.Rhel)]
    [InlineData("ID=centos\n", DistroFamily.Rhel)]
    [InlineData("ID=altlinux\n", DistroFamily.Alt)]
    [InlineData("ID=alt\n", DistroFamily.Alt)]
    [InlineData("ID=arch\n", DistroFamily.Unknown)]
    [InlineData("PRETTY_NAME=\"Some Linux\"\n", DistroFamily.Unknown)]
    public async Task DetectAsync_ParsesOsRelease(string content, DistroFamily expected)
    {
        var path = WriteOsRelease(content);
        var probe = new OsReleaseDistroProbe(new CommandsRunner(), path);

        Assert.Equal(expected, await probe.DetectAsync());
    }

    [Fact]
    public async Task DetectAsync_FallsBackToDnf_WhenOsReleaseMissing()
    {
        var probe = new OsReleaseDistroProbe(
            new CommandsRunner("dnf", "apt-get", "amneziawg-go"),
            Path.Combine(_tempDir, "absent"));

        Assert.Equal(DistroFamily.Rhel, await probe.DetectAsync());
    }

    [Fact]
    public async Task DetectAsync_FallsBackToApt_WhenOnlyAptPresent()
    {
        var probe = new OsReleaseDistroProbe(
            new CommandsRunner("apt-get"),
            Path.Combine(_tempDir, "absent"));

        Assert.Equal(DistroFamily.Debian, await probe.DetectAsync());
    }

    [Fact]
    public async Task DetectAsync_ReturnsUnknown_WhenNothingMatches()
    {
        var probe = new OsReleaseDistroProbe(
            new CommandsRunner(),
            Path.Combine(_tempDir, "absent"));

        Assert.Equal(DistroFamily.Unknown, await probe.DetectAsync());
    }

    private string WriteOsRelease(string content)
    {
        var path = Path.Combine(_tempDir, "os-release-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class CommandsRunner(params string[] commands) : IProcessRunner
    {
        public bool IsCommandAvailable(string command) => commands.Contains(command);

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

        public Task<ProcessResult> RunPrivilegedShellAsync(
            string script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public bool HasActivePrivilegedSession => false;
    }
}
