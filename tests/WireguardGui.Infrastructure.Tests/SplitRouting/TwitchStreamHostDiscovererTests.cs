using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public void ExtractHosts_ParsesSessionDataAndBase64Urls()
    {
        var liveVideo = Convert.ToBase64String(
            global::System.Text.Encoding.UTF8.GetBytes("https://1d144e.rufio.hls.live-video.net/v1/segment/x.ts"));
        var playlist = $"""
            #EXTM3U
            #EXT-X-SESSION-DATA:DATA-ID="NODE",VALUE="e8d2b6296c88.j.cloudfront.hls.ttvnw.net"
            #EXT-X-SESSION-DATA:DATA-ID="C",VALUE="{liveVideo}"
            """;

        var hosts = TwitchStreamHostDiscoverer.ExtractHosts(playlist);

        Assert.Contains("e8d2b6296c88.j.cloudfront.hls.ttvnw.net", hosts);
        Assert.Contains("1d144e.rufio.hls.live-video.net", hosts);
    }

    [Fact]
    public async Task DiscoverHostsAsync_RetriesOn429_ThenSucceeds()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)) },
            },
            GqlSuccess("token", "sig"),
            UsherSuccessNoMedia());
        var discoverer = CreateDiscoverer(handler);

        var hosts = await discoverer.DiscoverHostsAsync("somechannel", CancellationToken.None);

        Assert.NotEmpty(hosts);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task DiscoverHostsAsync_RetriesOn429Twice_ReturnsEmpty()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)) },
            },
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)) },
            },
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(0)) },
            });
        var discoverer = CreateDiscoverer(handler);

        var hosts = await discoverer.DiscoverHostsAsync("somechannel", CancellationToken.None);

        Assert.Empty(hosts);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task DiscoverHostsAsync_Offline_ReturnsEmptyWithoutRetries()
    {
        var handler = new SequenceHandler(GqlOffline());
        var discoverer = CreateDiscoverer(handler);

        var hosts = await discoverer.DiscoverHostsAsync("somechannel", CancellationToken.None);

        Assert.Empty(hosts);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task DiscoverHostsAsync_Timeout_RetriesThenSucceeds()
    {
        var handler = new SequenceHandler(
            new TimeoutResponse(),
            GqlSuccess("token", "sig"),
            UsherSuccessNoMedia());
        var discoverer = CreateDiscoverer(handler);

        var hosts = await discoverer.DiscoverHostsAsync("somechannel", CancellationToken.None);

        Assert.NotEmpty(hosts);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task DiscoverHostsAsync_TimeoutTwice_ReturnsEmpty()
    {
        var handler = new SequenceHandler(
            new TimeoutResponse(),
            new TimeoutResponse(),
            new TimeoutResponse());
        var discoverer = CreateDiscoverer(handler);

        var hosts = await discoverer.DiscoverHostsAsync("somechannel", CancellationToken.None);

        Assert.Empty(hosts);
        Assert.Equal(3, handler.RequestCount);
    }

    private static TwitchStreamHostDiscoverer CreateDiscoverer(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<TwitchStreamHostDiscoverer>.Instance);

    private static HttpResponseMessage GqlSuccess(string token, string signature) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"data\":{{\"streamPlaybackAccessToken\":{{\"value\":\"{token}\",\"signature\":\"{signature}\"}}}}}}",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage GqlOffline() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":{"streamPlaybackAccessToken":null}}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage UsherSuccess() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                #EXTM3U
                #EXT-X-STREAM-INF:BANDWIDTH=1000
                https://eun11.playlist.ttvnw.net/v1/playlist/abc.m3u8
                """,
                Encoding.UTF8,
                "application/x-mpegURL"),
        };

    private static HttpResponseMessage UsherSuccessNoMedia() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                #EXTM3U
                #EXT-X-SESSION-DATA:DATA-ID="NODE",VALUE="e8d2b6296c88.j.cloudfront.hls.ttvnw.net"
                """,
                Encoding.UTF8,
                "application/x-mpegURL"),
        };

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses;

        public SequenceHandler(params object[] responses)
        {
            _responses = new Queue<object>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count > 0)
            {
                var next = _responses.Dequeue();
                if (next is TimeoutResponse)
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout elapsing.");
                return Task.FromResult((HttpResponseMessage)next);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TimeoutResponse
    {
    }
}
