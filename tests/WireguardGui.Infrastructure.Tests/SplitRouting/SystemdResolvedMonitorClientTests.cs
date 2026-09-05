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
    public void TryParseQueryResult_ReadsAaaaRecords()
    {
        var json = """
            {
              "state": "success",
              "question": [{"name": "usher.ttvnw.net", "type": 28, "class": 1}],
              "answer": [
                {
                  "rr": {
                    "key": {"name": "usher.ttvnw.net", "type": 28, "class": 1},
                    "address": [32, 1, 13, 184, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
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
        Assert.Equal(["2001:db8::"], addresses.Select(a => a.ToString()));
    }

    [Fact]
    public void TryParseQueryResult_IgnoresNonSuccessState()
    {
        var json = """
            {
              "state": "failed",
              "question": [{"name": "usher.ttvnw.net", "type": 1, "class": 1}],
              "answer": []
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var ok = SystemdResolvedMonitorClient.TryParseQueryResult(
            doc.RootElement,
            out _,
            out _);

        Assert.False(ok);
    }
}
