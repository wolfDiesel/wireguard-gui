using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Handlers;
using WireguardGui.Application.Services;
using WireguardGui.Domain;

namespace WireguardGui.Application.Tests.Services;

public class SplitRoutingRefreshServiceTests
{
    private sealed class FakeTimer : ISplitRoutingTimer
    {
        public TimeSpan Interval { get; set; }
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public event EventHandler? Tick;

        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeResumeWatcher : ISystemResumeWatcher
    {
        public Func<CancellationToken, Task>? Handler { get; private set; }
        public void Start(Func<CancellationToken, Task> onResumed) => Handler = onResumed;
    }

    private sealed class FakeDnsMonitor : IResolvedDnsRouteMonitor
    {
        public bool IsRunning => false;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public Task StartAsync(VpnProfile profile, CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : ISplitRoutingRefreshNotifier
    {
        public int NotifyCount { get; private set; }
        public void NotifyRoutesRefreshed(int routeCount) => NotifyCount++;
    }

    private sealed class FakeSettingsStore(AppSettings settings) : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
        public Task SaveAsync(AppSettings s, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class FakeBackend : IWireGuardBackend
    {
        public BackendKind Kind => BackendKind.Native;
        public ConnectionState State { get; set; } = ConnectionState.Connected;
        public Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ConnectionState> GetConnectionStateAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(State);
        public Task ReimportFromConfigAsync(VpnProfile profile, bool connectAfter, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeBackendFactory(IWireGuardBackend backend) : IWireGuardBackendFactory
    {
        public IWireGuardBackend GetBackend(BackendKind kind) => backend;
    }

    private sealed class FakeUpdater : ISplitRoutingConfigUpdater
    {
        public int CallCount { get; private set; }
        public Task<SplitRoutingConfigUpdateResult> TryUpdateConfigAsync(
            VpnProfile profile,
            IProgress<SplitRoutingProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new SplitRoutingConfigUpdateResult(true, 5, "1.1.1.1/32", null));
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

    private static (SplitRoutingRefreshService Service, FakeTimer Timer, FakeDnsMonitor Dns, FakeNotifier Notifier, FakeUpdater Updater)
        CreateService(VpnProfile profile, ConnectionState state = ConnectionState.Connected)
    {
        var timer = new FakeTimer();
        var resume = new FakeResumeWatcher();
        var dns = new FakeDnsMonitor();
        var notifier = new FakeNotifier();
        var updater = new FakeUpdater();
        var backend = new FakeBackend { State = state };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProfileStore>(new FakeProfileStore(profile));
        services.AddSingleton<ISettingsStore>(new FakeSettingsStore(AppSettings.CreateDefault()));
        services.AddSingleton<IWireGuardBackendFactory>(new FakeBackendFactory(backend));
        services.AddSingleton<ISplitRoutingConfigUpdater>(updater);
        services.AddSingleton<IPolicyRoutingSetup>(new FakePolicyRouting());
        services.AddSingleton<ApplySplitRoutingHandler>();

        var provider = services.BuildServiceProvider();
        var service = new SplitRoutingRefreshService(
            provider,
            timer,
            resume,
            dns,
            notifier,
            NullLogger<SplitRoutingRefreshService>.Instance);

        return (service, timer, dns, notifier, updater);
    }

    [Fact]
    public async Task NotifyProfileConnected_StartsTimerAndDnsMonitor()
    {
        var profile = VpnProfile.Create("p", BackendKind.Native, "p") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Enabled = true, Twitch = true },
        };
        var (service, timer, dns, _, _) = CreateService(profile);

        service.NotifyProfileConnected(profile.Id);
        await Task.Delay(100);

        Assert.True(timer.Started);
        Assert.Equal(1, dns.StartCount);
    }

    [Fact]
    public async Task Tick_RefreshesRoutes_AndNotifies()
    {
        var profile = VpnProfile.Create("p", BackendKind.Native, "p") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Enabled = true, Twitch = true },
        };
        var (service, timer, _, notifier, updater) = CreateService(profile);

        service.NotifyProfileConnected(profile.Id);
        await Task.Delay(100);
        timer.RaiseTick();
        await Task.Delay(100);

        Assert.Equal(1, updater.CallCount);
        Assert.Equal(1, notifier.NotifyCount);
    }

    [Fact]
    public async Task BeginManualApply_BlocksTickRefresh()
    {
        var profile = VpnProfile.Create("p", BackendKind.Native, "p") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Enabled = true, Twitch = true },
        };
        var (service, timer, _, _, updater) = CreateService(profile);

        service.NotifyProfileConnected(profile.Id);
        await Task.Delay(100);

        using var scope = service.BeginManualApply();
        timer.RaiseTick();
        await Task.Delay(100);

        Assert.Equal(0, updater.CallCount);
    }

    [Fact]
    public async Task NotifyProfileDisconnected_StopsTimerAndDnsMonitor()
    {
        var profile = VpnProfile.Create("p", BackendKind.Native, "p") with
        {
            SplitRouting = SplitRoutingSettings.CreateDefault() with { Enabled = true, Twitch = true },
        };
        var (service, timer, dns, _, _) = CreateService(profile);

        service.NotifyProfileConnected(profile.Id);
        await Task.Delay(100);
        service.NotifyProfileDisconnected(profile.Id);
        await Task.Delay(100);

        Assert.True(timer.Stopped);
        Assert.Equal(1, dns.StopCount);
    }
}