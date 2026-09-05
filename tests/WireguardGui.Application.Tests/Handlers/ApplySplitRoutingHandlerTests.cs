using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Domain;

namespace WireguardGui.Application.Tests.Handlers;

public class ApplySplitRoutingHandlerTests
{
    [Fact]
    public async Task HandleAsync_NormalizesSettingsBeforeApply()
    {
        var profile = VpnProfile.Create("p", BackendKind.Nmcli, "p") with
        {
            SplitRouting = new SplitRoutingSettings(
                Enabled: true,
                Youtube: true,
                Telegram: false,
                Twitch: false,
                CustomDomains: ["  example.com ", "example.com"],
                IncludeCloudflare: false,
                MaxRoutes: 0),
        };

        var store = new CapturingProfileStore(profile);
        var backend = new TrackingBackend();
        var updater = new CapturingUpdater();
        var handler = new ApplySplitRoutingHandler(
            store,
            updater,
            new FakePolicyRouting(),
            new FakeBackendFactory(backend),
            NullLogger<ApplySplitRoutingHandler>.Instance);

        var result = await handler.HandleAsync(profile.Id);

        Assert.True(result.Success);
        var normalized = updater.LastProfile!.SplitRouting;
        Assert.Equal(SplitRoutingSettings.DefaultMaxRoutes, normalized.MaxRoutes);
        Assert.Single(normalized.CustomDomains);
        Assert.Equal("example.com", normalized.CustomDomains[0]);
    }

    private sealed class CapturingProfileStore(VpnProfile profile) : IProfileStore
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
        public Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ConnectionState> GetConnectionStateAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionState.Disconnected);
        public Task ReimportFromConfigAsync(VpnProfile profile, bool connectAfter, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBackendFactory(IWireGuardBackend backend) : IWireGuardBackendFactory
    {
        public IWireGuardBackend GetBackend(BackendKind kind) => backend;
    }

    private sealed class CapturingUpdater : ISplitRoutingConfigUpdater
    {
        public VpnProfile? LastProfile { get; private set; }

        public Task<SplitRoutingConfigUpdateResult> TryUpdateConfigAsync(
            VpnProfile profile,
            IProgress<SplitRoutingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.FromResult(new SplitRoutingConfigUpdateResult(false, 0, null, null));
        }
    }

    private sealed class FakePolicyRouting : IPolicyRoutingSetup
    {
        public bool IsAvailable => false;

        public Task PrepareConnectionAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PolicyRoutingApplyResult> ApplyAsync(
            VpnProfile profile,
            IReadOnlyList<string> routes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PolicyRoutingApplyResult(true, null));

        public Task<PolicyRoutingSyncResult> SyncRoutesAsync(
            VpnProfile profile,
            IReadOnlyList<string> routes,
            CancellationToken cancellationToken = default,
            bool force = false) =>
            Task.FromResult(new PolicyRoutingSyncResult(false, null));

        public Task AddHostRoutesAsync(
            VpnProfile profile,
            IReadOnlyList<string> hostCidrs,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task TeardownAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}