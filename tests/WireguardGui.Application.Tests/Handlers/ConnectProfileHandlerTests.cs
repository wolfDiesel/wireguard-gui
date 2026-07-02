using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Application.Tests.Handlers;

public class ConnectProfileHandlerTests
{
    [Fact]
    public async Task HandleAsync_SplitEnabledUnchangedConfig_UsesConnectNotReimport()
    {
        var profile = VpnProfile.Create("p", BackendKind.Nmcli, "p") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Enabled = true },
        };

        var store = new FakeProfileStore(profile);
        var backend = new TrackingBackend();
        var handler = new ConnectProfileHandler(
            store,
            new FakeBackendFactory(backend),
            new FakeUpdater(new SplitRoutingConfigUpdateResult(false, 5, "1.1.1.1/32", null)),
            NullLogger<ConnectProfileHandler>.Instance);

        var result = await handler.HandleAsync(profile.Id);

        Assert.True(result.Success);
        Assert.True(backend.ConnectCalled);
        Assert.False(backend.ReimportCalled);
    }

    private sealed class FakeProfileStore(VpnProfile profile) : IProfileStore
    {
        public string DataRoot => "/tmp";
        public Task<IReadOnlyList<VpnProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VpnProfile>>([profile]);
        public Task<VpnProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<VpnProfile?>(profile.Id == profileId ? profile : null);
        public Task SaveProfileAsync(VpnProfile p, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string GetProfileDirectory(string profileId) => $"/tmp/{profileId}";
        public string GetConfigPath(string profileId) => $"/tmp/{profileId}/wireguard.conf";
        public string GetConfigPath(VpnProfile p) => $"/tmp/{p.Id}/{p.ConfigFileName}";
    }

    private sealed class TrackingBackend : IWireGuardBackend
    {
        public BackendKind Kind => BackendKind.Nmcli;
        public bool ConnectCalled { get; private set; }
        public bool ReimportCalled { get; private set; }

        public Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
        {
            ConnectCalled = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ConnectionState> GetConnectionStateAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionState.Connected);

        public Task ReimportFromConfigAsync(VpnProfile profile, bool connectAfter, CancellationToken cancellationToken = default)
        {
            ReimportCalled = true;
            return Task.CompletedTask;
        }

        public Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeBackendFactory(IWireGuardBackend backend) : IWireGuardBackendFactory
    {
        public IWireGuardBackend GetBackend(BackendKind kind) => backend;
    }

    private sealed class FakeUpdater(SplitRoutingConfigUpdateResult result) : ISplitRoutingConfigUpdater
    {
        public Task<SplitRoutingConfigUpdateResult> TryUpdateConfigAsync(
            VpnProfile profile,
            IProgress<SplitRoutingProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}

public class DeleteProfileHandlerTests
{
    [Fact]
    public async Task HandleAsync_UnregisterFails_StillDeletesProfile()
    {
        var profile = VpnProfile.Create("p", BackendKind.Nmcli, "p");
        var store = new DeleteTrackingStore(profile);
        var backend = new FailingUnregisterBackend();
        var handler = new DeleteProfileHandler(
            store,
            new FakeBackendFactory(backend),
            NullLogger<DeleteProfileHandler>.Instance);

        var result = await handler.HandleAsync(profile.Id);

        Assert.True(result.Success);
        Assert.Equal("nm error", result.WarningMessage);
        Assert.True(store.Deleted);
    }

    private sealed class DeleteTrackingStore(VpnProfile profile) : IProfileStore
    {
        public bool Deleted { get; private set; }
        public string DataRoot => "/tmp";
        public Task<IReadOnlyList<VpnProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VpnProfile>>([profile]);
        public Task<VpnProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<VpnProfile?>(profile);
        public Task SaveProfileAsync(VpnProfile p, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
        public string GetProfileDirectory(string profileId) => $"/tmp/{profileId}";
        public string GetConfigPath(string profileId) => $"/tmp/{profileId}/wireguard.conf";
        public string GetConfigPath(VpnProfile p) => $"/tmp/{p.Id}/{p.ConfigFileName}";
    }

    private sealed class FailingUnregisterBackend : IWireGuardBackend
    {
        public BackendKind Kind => BackendKind.Nmcli;
        public Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            throw new WireGuardOperationException("nm error");
        public Task<ConnectionState> GetConnectionStateAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionState.Disconnected);
        public Task ReimportFromConfigAsync(VpnProfile profile, bool connectAfter, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeBackendFactory(IWireGuardBackend backend) : IWireGuardBackendFactory
    {
        public IWireGuardBackend GetBackend(BackendKind kind) => backend;
    }
}
