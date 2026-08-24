using WireguardGui.Domain;

namespace WireguardGui.App.Tests;

public class TunnelDnsServersTests
{
    [Fact]
    public void TryParse_Empty_ReturnsTrue()
    {
        Assert.True(TunnelDnsServers.TryParse(null, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_ValidSingle_Normalizes()
    {
        Assert.True(TunnelDnsServers.TryParse("10.8.0.1", out var normalized, out _));
        Assert.Equal("10.8.0.1", normalized);
    }

    [Fact]
    public void TryParse_ValidMultiple_Deduplicates()
    {
        Assert.True(TunnelDnsServers.TryParse("10.8.0.1, 1.1.1.1, 10.8.0.1", out var normalized, out _));
        Assert.Equal("10.8.0.1, 1.1.1.1", normalized);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsError()
    {
        Assert.False(TunnelDnsServers.TryParse("not-an-ip", out _, out var error));
        Assert.Contains("not-an-ip", error);
    }

    [Theory]
    [InlineData("10.8.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void IsTunnelLocal_Classifies(string ip, bool expected) =>
        Assert.Equal(expected, TunnelDnsServers.IsTunnelLocal(ip));

    [Fact]
    public void ShouldCaptureAllDns_WhenAnyServersPresent()
    {
        Assert.True(TunnelDnsServers.ShouldCaptureAllDns(["10.8.0.1"]));
        Assert.True(TunnelDnsServers.ShouldCaptureAllDns(["8.8.8.8"]));
        Assert.False(TunnelDnsServers.ShouldCaptureAllDns([]));
    }

    [Fact]
    public void ResolveOrDefault_UsesGoogleDns()
    {
        Assert.Equal("8.8.8.8", TunnelDnsServers.Default);
        Assert.Equal("8.8.8.8", TunnelDnsServers.ResolveOrDefault(null));
        Assert.Equal("8.8.8.8", TunnelDnsServers.ResolveOrDefault(""));
        Assert.Equal("1.1.1.1", TunnelDnsServers.ResolveOrDefault("1.1.1.1"));
    }
}
