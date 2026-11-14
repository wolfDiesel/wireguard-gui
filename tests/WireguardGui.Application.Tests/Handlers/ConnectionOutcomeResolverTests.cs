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

    [Theory]
    [InlineData("awg0", true)]
    [InlineData("awg-office", true)]
    [InlineData("a.b+c=d", true)]
    [InlineData("", false)]
    [InlineData("bad name", false)]
    [InlineData("toolongname123456", false)]
    public void IsValidAmneziaWgInterfaceName_ValidatesAwgQuickRules(string name, bool expected) =>
        Assert.Equal(expected, VpnProfileNaming.IsValidAmneziaWgInterfaceName(name));

    [Theory]
    [InlineData("Amnezia WG office", "Amnezia-WG-offi")]
    [InlineData("wg 0", "wg-0")]
    [InlineData("!!!", "awg")]
    [InlineData("", "awg")]
    [InlineData("a-b", "a-b")]
    public void ToAmneziaWgInterfaceName_NormalizesCandidate(string candidate, string expected) =>
        Assert.Equal(expected, VpnProfileNaming.ToAmneziaWgInterfaceName(candidate));

    [Fact]
    public void MakeUniqueAmneziaWgInterfaceName_AppendsSuffix()
    {
        var name = VpnProfileNaming.MakeUniqueAmneziaWgInterfaceName("awg0", ["awg0", "awg0-2"]);

        Assert.Equal("awg0-3", name);
    }

    [Fact]
    public void MakeUniqueAmneziaWgInterfaceName_KeepsLengthLimit()
    {
        var name = VpnProfileNaming.MakeUniqueAmneziaWgInterfaceName(
            "abcdefghijklmno",
            ["abcdefghijklmno"]);

        Assert.Equal("abcdefghijklm-2", name);
        Assert.True(VpnProfileNaming.IsValidAmneziaWgInterfaceName(name));
    }

    [Fact]
    public void MakeUniqueAmneziaWgInterfaceName_ComparesCaseInsensitively()
    {
        var name = VpnProfileNaming.MakeUniqueAmneziaWgInterfaceName("awg0", ["AWG0"]);

        Assert.Equal("awg0-2", name);
    }
}
