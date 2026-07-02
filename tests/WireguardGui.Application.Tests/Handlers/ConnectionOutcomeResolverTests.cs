using WireguardGui.Application.Contracts;
using WireguardGui.Application.Services;
using WireguardGui.Domain;

namespace WireguardGui.Application.Tests.Handlers;

public class ConnectionOutcomeResolverTests
{
    [Fact]
    public void ResolveAfterFailure_Connected_ReturnsSuccessWithWarning()
    {
        var result = ConnectionOutcomeResolver.ResolveAfterFailure(ConnectionState.Connected, "ignored error");
        Assert.True(result.Success);
        Assert.Equal("ignored error", result.WarningMessage);
    }

    [Fact]
    public void ResolveAfterFailure_Disconnected_ReturnsFailure()
    {
        var result = ConnectionOutcomeResolver.ResolveAfterFailure(ConnectionState.Disconnected, "fail");
        Assert.False(result.Success);
        Assert.Equal(OperationErrorCode.ConnectionFailed, result.ErrorCode);
    }
}

public class VpnProfileNamingTests
{
    [Theory]
    [InlineData("wg-office", true)]
    [InlineData("bad name", false)]
    [InlineData("bad;name", false)]
    public void IsValidConnectionName_ValidatesAsciiIdentifier(string name, bool expected) =>
        Assert.Equal(expected, VpnProfileNaming.IsValidConnectionName(name));
}
