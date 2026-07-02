using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Application.Tests.Handlers;

public class ImportProfileHandlerTests
{
    [Fact]
    public async Task HandleAsync_RejectsUnavailableBackend()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wg-gui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "test.conf");
        await File.WriteAllTextAsync(configPath, SampleConfig);

        try
        {
            var handler = CreateHandler(new AlwaysUnavailableProbe());

            var result = await handler.HandleAsync(
                new ImportProfileRequestDto(configPath, BackendKind.Nmcli));

            Assert.False(result.Success);
            Assert.Contains("unavailable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_SavesProfile_WithoutPrivilegedImport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wg-gui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "office.conf");
        await File.WriteAllTextAsync(configPath, SampleConfigWithName);

        try
        {
            var store = new JsonProfileStore(Path.Combine(tempDir, "data"), NullLogger<JsonProfileStore>.Instance);
            var handler = CreateHandler(new AlwaysAvailableProbe(), store);

            var result = await handler.HandleAsync(
                new ImportProfileRequestDto(configPath, BackendKind.Nmcli));

            Assert.True(result.Success);
            Assert.NotNull(result.ProfileId);

            var profile = await store.GetProfileAsync(result.ProfileId!);
            Assert.NotNull(profile);
            Assert.Equal("office", profile.Name);
            Assert.Equal("office", profile.ConnectionName);
            Assert.True(File.Exists(store.GetConfigPath(profile!)));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_Native_UsesInterfaceNameAsConnection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wg-gui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "myvpn.conf");
        await File.WriteAllTextAsync(configPath, SampleConfigWithName);

        try
        {
            var store = new JsonProfileStore(Path.Combine(tempDir, "data"), NullLogger<JsonProfileStore>.Instance);
            var handler = CreateHandler(new AlwaysAvailableProbe(), store);

            var result = await handler.HandleAsync(
                new ImportProfileRequestDto(configPath, BackendKind.Native));

            Assert.True(result.Success);
            var profile = await store.GetProfileAsync(result.ProfileId!);
            Assert.Equal("wg-office", profile!.ConnectionName);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static ImportProfileHandler CreateHandler(
        WireguardGui.Application.Abstractions.ISystemCapabilityProbe probe,
        JsonProfileStore? store = null)
    {
        store ??= new JsonProfileStore(
            Path.Combine(Path.GetTempPath(), "wg-gui-" + Guid.NewGuid().ToString("N")),
            NullLogger<JsonProfileStore>.Instance);
        var importer = new ProfileImporter(
            store,
            new WireGuardConfigValidator(),
            new WireGuardConfigParser(),
            NullLogger<ProfileImporter>.Instance);
        return new ImportProfileHandler(probe, importer);
    }

    private const string SampleConfig = """
        [Interface]
        PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        [Peer]
        PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Endpoint = 1.2.3.4:51820
        """;

    private const string SampleConfigWithName = """
        [Interface]
        PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Name = wg-office
        [Peer]
        PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
        Endpoint = 1.2.3.4:51820
        """;

    private sealed class AlwaysUnavailableProbe : WireguardGui.Application.Abstractions.ISystemCapabilityProbe
    {
        public Task<WireguardGui.Application.Abstractions.BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WireguardGui.Application.Abstractions.BackendCapability(
                BackendKind.Native, false, ["wg"], "", ""));

        public Task<WireguardGui.Application.Abstractions.BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WireguardGui.Application.Abstractions.BackendCapability(
                BackendKind.Nmcli, false, ["nmcli"], "", ""));

        public Task<IReadOnlyList<WireguardGui.Application.Abstractions.BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WireguardGui.Application.Abstractions.BackendCapability>>([
                new(BackendKind.Native, false, ["wg"], "", ""),
                new(BackendKind.Nmcli, false, ["nmcli"], "", ""),
            ]);
    }

    private sealed class AlwaysAvailableProbe : WireguardGui.Application.Abstractions.ISystemCapabilityProbe
    {
        public Task<WireguardGui.Application.Abstractions.BackendCapability> ProbeNativeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WireguardGui.Application.Abstractions.BackendCapability(
                BackendKind.Native, true, [], "", ""));

        public Task<WireguardGui.Application.Abstractions.BackendCapability> ProbeNmcliAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WireguardGui.Application.Abstractions.BackendCapability(
                BackendKind.Nmcli, true, [], "", ""));

        public Task<IReadOnlyList<WireguardGui.Application.Abstractions.BackendCapability>> ProbeAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WireguardGui.Application.Abstractions.BackendCapability>>([
                new(BackendKind.Native, true, [], "", ""),
                new(BackendKind.Nmcli, true, [], "", ""),
            ]);
    }
}
