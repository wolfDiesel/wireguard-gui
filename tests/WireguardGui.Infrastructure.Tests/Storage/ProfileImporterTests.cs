using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.Storage;

public class ProfileImporterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonProfileStore _store;
    private readonly ProfileImporter _importer;

    public ProfileImporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "wg-gui-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new JsonProfileStore(Path.Combine(_tempDir, "data"), NullLogger<JsonProfileStore>.Instance);
        _importer = new ProfileImporter(
            _store,
            new WireGuardConfigValidator(),
            new WireGuardConfigParser(),
            NullLogger<ProfileImporter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(BackendKind.Native)]
    [InlineData(BackendKind.Nmcli)]
    public async Task ImportFromFileAsync_RejectsAmneziaWgConfig_ForWireGuardBackends(BackendKind backend)
    {
        var path = WriteConfig("amnezia.conf", AmneziaWgConfig);

        var result = await _importer.ImportFromFileAsync(path, backend);

        Assert.False(result.Success);
        Assert.Null(result.ProfileId);
        Assert.Contains("AmneziaWG", result.ErrorMessage ?? string.Empty);
        Assert.Empty(await _store.ListProfilesAsync());
    }

    [Fact]
    public async Task ImportFromFileAsync_ImportsAmneziaWgConfig()
    {
        var path = WriteConfig("amnezia.conf", AmneziaWgConfigWithName("awg-office"));

        var result = await _importer.ImportFromFileAsync(path, BackendKind.AmneziaWg);

        Assert.True(result.Success);
        var profile = await _store.GetProfileAsync(result.ProfileId!);
        Assert.NotNull(profile);
        Assert.Equal(BackendKind.AmneziaWg, profile!.Backend);
        Assert.Equal("awg-office", profile.ConnectionName);
        Assert.Equal("awg-office.conf", profile.ConfigFileName);
        Assert.True(VpnProfileNaming.IsValidAmneziaWgInterfaceName(profile.ConnectionName));

        var saved = await File.ReadAllTextAsync(_store.GetConfigPath(profile));
        Assert.DoesNotContain("Name =", saved);
        Assert.Contains("Jc = 4", saved);
        Assert.Contains("H1 = 123456", saved);
        Assert.Contains("PrivateKey = ", saved);
    }

    [Fact]
    public async Task ImportFromFileAsync_NormalizesLongAmneziaWgInterfaceName()
    {
        var path = WriteConfig("amnezia-vpn-office-tunnel.conf", AmneziaWgConfig);

        var result = await _importer.ImportFromFileAsync(path, BackendKind.AmneziaWg);

        Assert.True(result.Success);
        var profile = await _store.GetProfileAsync(result.ProfileId!);
        Assert.NotNull(profile);
        Assert.Equal("amnezia-vpn-off", profile!.ConnectionName);
        Assert.Equal("amnezia-vpn-off.conf", profile.ConfigFileName);
        Assert.Equal("amnezia-vpn-office-tunnel", profile.Name);
    }

    [Fact]
    public async Task ImportFromFileAsync_AppendsSuffix_WhenInterfaceNameTaken()
    {
        var firstPath = WriteConfig("first.conf", AmneziaWgConfigWithName("awg-office"));
        var secondPath = WriteConfig("second.conf", AmneziaWgConfigWithName("awg-office"));

        var firstResult = await _importer.ImportFromFileAsync(firstPath, BackendKind.AmneziaWg);
        var secondResult = await _importer.ImportFromFileAsync(secondPath, BackendKind.AmneziaWg);

        Assert.True(firstResult.Success);
        Assert.True(secondResult.Success);
        var secondProfile = await _store.GetProfileAsync(secondResult.ProfileId!);
        Assert.Equal("awg-office-2", secondProfile!.ConnectionName);
    }

    [Fact]
    public async Task ImportFromFileAsync_AllowsPlainWireGuardConfig_ForAmneziaWgBackend()
    {
        var path = WriteConfig("plain.conf", PlainConfig);

        var result = await _importer.ImportFromFileAsync(path, BackendKind.AmneziaWg);

        Assert.True(result.Success);
        var profile = await _store.GetProfileAsync(result.ProfileId!);
        Assert.Equal(BackendKind.AmneziaWg, profile!.Backend);
        Assert.Equal("plain", profile.ConnectionName);
    }

    private string WriteConfig(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static string AmneziaWgConfigWithName(string name) => $"""
        [Interface]
        PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Name = {name}
        Jc = 4
        Jmin = 50
        Jmax = 1000
        S1 = 20
        S2 = 20
        H1 = 123456
        [Peer]
        PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Endpoint = 1.2.3.4:51820
        AllowedIPs = 0.0.0.0/0
        """;

    private const string AmneziaWgConfig = """
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
        AllowedIPs = 0.0.0.0/0
        """;

    private const string PlainConfig = """
        [Interface]
        PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        [Peer]
        PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Endpoint = 1.2.3.4:51820
        """;
}
