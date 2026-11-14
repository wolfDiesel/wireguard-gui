using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.Storage;

public class ConfigKindProbeTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "wg-kind-" + Guid.NewGuid().ToString("N"));

    private readonly ConfigKindProbe _probe = new(new WireGuardConfigParser());

    public ConfigKindProbeTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task DetectAsync_ReturnsAmneziaWg_ForObfuscatedConfig()
    {
        var path = WriteConfig("awg.conf", """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Jc = 4
            Jmin = 50
            Jmax = 1000
            S1 = 20
            S2 = 20
            H1 = 123456
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = 1.2.3.4:51820
            """);

        Assert.Equal(ConfigKind.AmneziaWg, await _probe.DetectAsync(path));
    }

    [Fact]
    public async Task DetectAsync_ReturnsWireGuard_ForPlainConfig()
    {
        var path = WriteConfig("wg.conf", """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Address = 10.8.0.5/32
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = 1.2.3.4:51820
            AllowedIPs = 0.0.0.0/0
            """);

        Assert.Equal(ConfigKind.WireGuard, await _probe.DetectAsync(path));
    }

    [Fact]
    public async Task DetectAsync_ReturnsWireGuard_ForMissingFile() =>
        Assert.Equal(ConfigKind.WireGuard, await _probe.DetectAsync(Path.Combine(_tempDir, "absent.conf")));

    private string WriteConfig(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
