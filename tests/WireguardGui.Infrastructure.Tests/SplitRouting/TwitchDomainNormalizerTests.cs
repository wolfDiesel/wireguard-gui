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
}
