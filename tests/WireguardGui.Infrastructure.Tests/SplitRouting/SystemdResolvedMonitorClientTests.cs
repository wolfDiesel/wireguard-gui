using System.Net;
using System.Text.Json;
using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class SystemdResolvedMonitorClientTests
{
    [Fact]
    public void TryParseQueryResult_ReadsARecords()
    {
        var json = """
            {
              "state": "success",
              "question": [{"name": "usher.ttvnw.net", "type": 1, "class": 1}],
              "answer": [
                {
                  "rr": {
                    "key": {"name": "usher.ttvnw.net", "type": 1, "class": 1},
                    "address": [18, 239, 208, 48]
                  }
                }
              ]
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var ok = SystemdResolvedMonitorClient.TryParseQueryResult(
            doc.RootElement,
            out var host,
            out var addresses);

        Assert.True(ok);
        Assert.Equal("usher.ttvnw.net", host);
        Assert.Equal(["18.239.208.48"], addresses.Select(a => a.ToString()));
    }

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
    public void TryParseResolvectlMonitorLine_IgnoresQueries()
    {
        Assert.False(ResolvedDnsRouteMonitor.TryParseResolvectlMonitorLine(
            "→ Q: usher.ttvnw.net IN A",
            out _,
            out _));
    }
}
