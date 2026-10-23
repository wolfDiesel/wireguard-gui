using System.Net;
using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class ResolvedDnsRouteMonitorTests
{
    [Fact]
    public void TryParseResolvectlMonitorLine_ReadsARecord()
    {
        var ok = ResolvedDnsRouteMonitor.TryParseResolvectlMonitorLine(
            "← A: usher.ttvnw.net IN A 18.239.208.48",
            out var host,
            out var address);

        Assert.True(ok);
        Assert.Equal("usher.ttvnw.net", host);
        Assert.Equal(IPAddress.Parse("18.239.208.48"), address);
    }

    [Fact]
    public void TryParseResolvectlMonitorLine_ReadsAaaaRecord()
    {
        var ok = ResolvedDnsRouteMonitor.TryParseResolvectlMonitorLine(
            "<- A: usher.ttvnw.net IN AAAA 2001:db8::1",
            out var host,
            out var address);

        Assert.True(ok);
        Assert.Equal("usher.ttvnw.net", host);
        Assert.Equal(IPAddress.Parse("2001:db8::1"), address);
    }

    [Fact]
    public void TryParseResolvectlMonitorLine_TrimsTrailingDot()
    {
        var ok = ResolvedDnsRouteMonitor.TryParseResolvectlMonitorLine(
            "← A: usher.ttvnw.net. IN A 18.239.208.48",
            out var host,
            out _);

        Assert.True(ok);
        Assert.Equal("usher.ttvnw.net", host);
    }

    [Fact]
    public void TryParseResolvectlMonitorLine_IgnoresOtherRecords()
    {
        Assert.False(ResolvedDnsRouteMonitor.TryParseResolvectlMonitorLine(
            "← A: usher.ttvnw.net IN CNAME usher.ttvnw.net.edgekey.net",
            out _,
            out _));
        Assert.False(ResolvedDnsRouteMonitor.TryParseResolvectlMonitorLine(
            "Query usher.ttvnw.net IN A",
            out _,
            out _));
    }
}
