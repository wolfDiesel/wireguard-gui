using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class TwitchDomainNormalizerTests
{
    [Theory]
    [InlineData("*.live-video.net", "live-video.net")]
    [InlineData("video-edge-*.abs.hls.ttvnw.net", "abs.hls.ttvnw.net")]
    [InlineData("twitch.tv", "twitch.tv")]
    public void NormalizeOne_ExpandsWildcardPatterns(string input, string expected) =>
        Assert.Equal(expected, TwitchDomainNormalizer.NormalizeOne(input));

    [Fact]
    public void Normalize_DeduplicatesDomains()
    {
        var result = TwitchDomainNormalizer.Normalize(["twitch.tv", "Twitch.TV", "www.twitch.tv"]);
        Assert.Equal(2, result.Count);
        Assert.Contains("twitch.tv", result);
        Assert.Contains("www.twitch.tv", result);
    }

    [Fact]
    public void Normalize_SkipsKnownNonResolvableParents()
    {
        var result = TwitchDomainNormalizer.Normalize(
        [
            "usher.ttvnw.net",
            "*.abs.hls.ttvnw.net",
            "*.live-video.net",
            "*.j.cloudfront.hls.ttvnw.net",
            "eun11.playlist.ttvnw.net",
        ]);

        Assert.Contains("usher.ttvnw.net", result);
        Assert.Contains("eun11.playlist.ttvnw.net", result);
        Assert.DoesNotContain("abs.hls.ttvnw.net", result);
        Assert.DoesNotContain("live-video.net", result);
        Assert.DoesNotContain("j.cloudfront.hls.ttvnw.net", result);
    }
}
