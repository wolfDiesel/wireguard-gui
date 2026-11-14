using WireguardGui.Application.Handlers;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Application.Tests.Handlers;

public class DetectConfigKindHandlerTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "wg-kind-handler-" + Guid.NewGuid().ToString("N"));

    private readonly DetectConfigKindHandler _handler = new(new ConfigKindProbe(new WireGuardConfigParser()));

    public DetectConfigKindHandlerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task HandleAsync_DetectsAmneziaWgConfig()
    {
        var path = Path.Combine(_tempDir, "amnezia.conf");
        await File.WriteAllTextAsync(path, """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Jc = 4
            H1 = 123456
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = 1.2.3.4:51820
            """);

        Assert.Equal(ConfigKind.AmneziaWg, await _handler.HandleAsync(path));
    }

    [Fact]
    public async Task HandleAsync_ReturnsWireGuard_WhenFileMissing() =>
        Assert.Equal(
            ConfigKind.WireGuard,
            await _handler.HandleAsync(Path.Combine(_tempDir, "absent.conf")));
}
