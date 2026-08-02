using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class TwitchStreamHostDiscovererTests
{
    [Fact]
    public void ExtractHosts_ParsesAbsoluteUrls()
    {
        var playlist = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=1000
            https://eun11.playlist.ttvnw.net/v1/playlist/abc.m3u8
            #EXTINF:2
            https://e8d2b6296c88.j.cloudfront.hls.ttvnw.net/v1/segment/1.ts
            """;

        var hosts = TwitchStreamHostDiscoverer.ExtractHosts(playlist);

        Assert.Contains("eun11.playlist.ttvnw.net", hosts);
        Assert.Contains("e8d2b6296c88.j.cloudfront.hls.ttvnw.net", hosts);
    }
}
