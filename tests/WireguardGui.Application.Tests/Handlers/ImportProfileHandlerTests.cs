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
            var store = new JsonProfileStore(Path.Combine(tempDir, "data"));
            var handler = CreateHandler(store, new AlwaysUnavailableProbe());

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
            var store = new JsonProfileStore(Path.Combine(tempDir, "data"));
            var handler = CreateHandler(store, new AlwaysAvailableProbe());

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

    private static ImportProfileHandler CreateHandler(
        JsonProfileStore store,
        WireguardGui.Application.Abstractions.ISystemCapabilityProbe probe) =>
        new(
            store,
            new WireGuardConfigValidator(),
            new WireGuardConfigParser(),
            probe,
            NullLogger<ImportProfileHandler>.Instance);

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
