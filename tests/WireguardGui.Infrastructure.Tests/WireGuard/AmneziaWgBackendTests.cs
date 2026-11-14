using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.Tests.Fakes;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.WireGuard;

public class AmneziaWgBackendTests
{
    [Fact]
    public void Kind_IsAmneziaWg()
    {
        var (backend, _, _) = Create();

        Assert.Equal(BackendKind.AmneziaWg, backend.Kind);
    }

    [Fact]
    public async Task Connect_UsesAwgQuickUp_WithPrivileges()
    {
        var (backend, runner, store) = Create();
        var profile = CreateProfile();

        await backend.ConnectAsync(profile);

        Assert.Contains($"awg-quick up {store.GetConfigPath(profile)}", runner.CommandLines);
        Assert.True(runner.PrivilegedCallCount > 0, "Expected awg-quick up via privileged session");
    }

    [Fact]
    public async Task Connect_Throws_OnFailure()
    {
        var (backend, runner, store) = Create();
        var profile = CreateProfile();
        runner.CommandResults[$"awg-quick up {store.GetConfigPath(profile)}"] =
            new ProcessResult(1, string.Empty, "RTNETLINK answers: Operation not permitted");

        await Assert.ThrowsAsync<WireGuardOperationException>(() => backend.ConnectAsync(profile));
    }

    [Theory]
    [InlineData("`awg0' is not an AmneziaWG interface")]
    [InlineData("`awg0' is not a WireGuard interface")]
    [InlineData("Cannot find device \"awg0\"")]
    [InlineData("WireGuard interface `awg0' does not exist")]
    public async Task Disconnect_ToleratesMissingInterface(string error)
    {
        var (backend, runner, store) = Create();
        var profile = CreateProfile();
        runner.CommandResults[$"awg-quick down {store.GetConfigPath(profile)}"] =
            new ProcessResult(1, string.Empty, error);

        await backend.DisconnectAsync(profile);
    }

    [Fact]
    public async Task Disconnect_Throws_OnOtherErrors()
    {
        var (backend, runner, store) = Create();
        var profile = CreateProfile();
        runner.CommandResults[$"awg-quick down {store.GetConfigPath(profile)}"] =
            new ProcessResult(1, string.Empty, "Operation not permitted");

        await Assert.ThrowsAsync<WireGuardOperationException>(() => backend.DisconnectAsync(profile));
    }

    [Fact]
    public async Task GetConnectionState_ReadsAwgShow_ExactMatch()
    {
        var (backend, runner, _) = Create();
        runner.AwgShowOutput = """
            interface: awg0
              public key: abc
            interface: awg-office
              public key: def
            """;

        var state = await backend.GetConnectionStateAsync(CreateProfile("awg-office"));

        Assert.Equal(ConnectionState.Connected, state);
        Assert.Contains("awg show", runner.CommandLines);
        Assert.Equal(0, runner.PrivilegedCallCount);
    }

    [Fact]
    public async Task GetConnectionState_ReturnsDisconnected_WhenInterfaceAbsent()
    {
        var (backend, runner, _) = Create();
        runner.AwgShowOutput = "interface: awg0\n  public key: abc\n";

        var state = await backend.GetConnectionStateAsync(CreateProfile("awg-office"));

        Assert.Equal(ConnectionState.Disconnected, state);
    }

    [Fact]
    public async Task GetConnectionState_FallsBackToLinkProbe_WhenShowFails()
    {
        var (backend, runner, _) = Create();
        runner.CommandResults["awg show"] = new ProcessResult(1, string.Empty, "Unable to access interface: Operation not permitted");

        var state = await backend.GetConnectionStateAsync(CreateProfile("awg-office"));

        Assert.Equal(ConnectionState.Connected, state);
        Assert.Contains("ip link show dev awg-office", runner.CommandLines);
    }

    [Fact]
    public async Task GetConnectionState_ReturnsDisconnected_WhenLinkIsMissing()
    {
        var (backend, runner, _) = Create();
        runner.CommandResults["awg show"] = new ProcessResult(1, string.Empty, "Permission denied");
        runner.CommandResults["ip link show dev awg-office"] =
            new ProcessResult(1, string.Empty, "Device \"awg-office\" does not exist.");

        var state = await backend.GetConnectionStateAsync(CreateProfile("awg-office"));

        Assert.Equal(ConnectionState.Disconnected, state);
    }

    [Fact]
    public async Task GetConnectionState_ReturnsUnknown_WhenShowAndLinkProbeFail()
    {
        var (backend, runner, _) = Create();
        runner.CommandResults["awg show"] = new ProcessResult(1, string.Empty, "Permission denied");
        runner.CommandResults["ip link show dev awg-office"] =
            new ProcessResult(1, string.Empty, "RTNETLINK answers: Operation not permitted");

        var state = await backend.GetConnectionStateAsync(CreateProfile("awg-office"));

        Assert.Equal(ConnectionState.Unknown, state);
    }

    [Fact]
    public async Task GetConnectionState_UsesPrivilegedSession_WhenActive()
    {
        var (backend, runner, _) = Create();
        runner.HasActivePrivilegedSession = true;
        runner.AwgShowOutput = "interface: awg-office\n  public key: abc\n";

        var state = await backend.GetConnectionStateAsync(CreateProfile("awg-office"));

        Assert.Equal(ConnectionState.Connected, state);
        Assert.True(runner.PrivilegedCallCount > 0, "Expected awg show via privileged session");
    }

    [Fact]
    public async Task Reimport_Reconnects_WhenConnected()
    {
        var (backend, runner, _) = Create();
        runner.AwgShowOutput = "interface: awg-office\n  public key: abc\n";
        var profile = CreateProfile("awg-office");

        await backend.ReimportFromConfigAsync(profile, connectAfter: true);

        var down = runner.CommandLines.FindIndex(static line => line.StartsWith("awg-quick down", StringComparison.Ordinal));
        var up = runner.CommandLines.FindIndex(static line => line.StartsWith("awg-quick up", StringComparison.Ordinal));
        Assert.True(down >= 0, "Expected awg-quick down");
        Assert.True(up > down, "Expected awg-quick up after down");
    }

    [Fact]
    public async Task Reimport_Skips_WhenDisconnectedAndNotConnectingAfter()
    {
        var (backend, runner, _) = Create();
        runner.AwgShowOutput = string.Empty;

        await backend.ReimportFromConfigAsync(CreateProfile(), connectAfter: false);

        Assert.Equal(["awg show"], runner.CommandLines);
    }

    [Fact]
    public async Task Unregister_Disconnects()
    {
        var (backend, runner, store) = Create();
        var profile = CreateProfile();

        await backend.UnregisterAsync(profile);

        Assert.Contains($"awg-quick down {store.GetConfigPath(profile)}", runner.CommandLines);
    }

    [Fact]
    public void Factory_ReturnsAmneziaWgBackend()
    {
        var (backend, runner, store) = Create();
        var factory = new WireGuardBackendFactory(
            new NativeWireGuardBackend(runner, store),
            new NmcliWireGuardBackend(runner, store, new WireGuardConfigParser(), NullLogger<NmcliWireGuardBackend>.Instance),
            backend);

        Assert.Same(backend, factory.GetBackend(BackendKind.AmneziaWg));
        Assert.IsType<NativeWireGuardBackend>(factory.GetBackend(BackendKind.Native));
    }

    private static (AmneziaWgBackend Backend, FakeProcessRunner Runner, JsonProfileStore Store) Create()
    {
        var runner = new FakeProcessRunner();
        var store = TestStoreFactory.Create(
            Path.Combine(Path.GetTempPath(), "wg-awg-" + Guid.NewGuid().ToString("N")));
        return (new AmneziaWgBackend(runner, store), runner, store);
    }

    private static VpnProfile CreateProfile(string connectionName = "awg-office") =>
        VpnProfile.Create("p", BackendKind.AmneziaWg, connectionName);
}
