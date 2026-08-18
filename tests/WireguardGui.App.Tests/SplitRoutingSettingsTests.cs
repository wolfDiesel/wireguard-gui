using WireguardGui.Domain;

namespace WireguardGui.App.Tests;

public class TwitchChannelNamingTests
{
    [Theory]
    [InlineData("SomeChannel", "somechannel")]
    [InlineData("@Streamer", "streamer")]
    [InlineData("https://www.twitch.tv/xqc", "xqc")]
    [InlineData("twitch.tv/foo/videos", "foo")]
    [InlineData("bad channel", null)]
    [InlineData("", null)]
    public void Normalize_ChannelLogins(string? input, string? expected) =>
        Assert.Equal(expected, TwitchChannelNaming.Normalize(input));
}

public class SplitRoutingSettingsTests
{
    [Fact]
    public void Normalize_ClampsMaxRoutes()
    {
        var settings = new SplitRoutingSettings(false, true, true, false, [], false, 0);
        var normalized = settings.Normalize();
        Assert.Equal(SplitRoutingSettings.DefaultMaxRoutes, normalized.MaxRoutes);
    }

    [Fact]
    public void Normalize_DeduplicatesCustomDomains()
    {
        var settings = new SplitRoutingSettings(
            true, false, false, false, ["A.com", "a.com", " b.com "], false, 200);
        var normalized = settings.Normalize();
        Assert.Equal(2, normalized.CustomDomains.Count);
    }

    [Fact]
    public void Normalize_NormalizesTwitchChannel()
    {
        var settings = new SplitRoutingSettings(
            true, false, false, true, [], false, 200, "https://twitch.tv/Test_Channel");
        var normalized = settings.Normalize();
        Assert.Equal("test_channel", normalized.TwitchChannel);
    }

    [Fact]
    public void NeedsDnsRouteRefresh_WhenTwitchOrCustom()
    {
        Assert.True(new SplitRoutingSettings(true, false, false, true, [], false, 200)
            .NeedsDnsRouteRefresh);
        Assert.True(new SplitRoutingSettings(true, false, false, false, ["a.com"], false, 200)
            .NeedsDnsRouteRefresh);
        Assert.False(new SplitRoutingSettings(true, true, false, false, [], false, 200)
            .NeedsDnsRouteRefresh);
    }
}

public class AppSettingsTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(120, 120)]
    [InlineData(121, 120)]
    public void ClampRefreshMinutes(int input, int expected) =>
        Assert.Equal(expected, AppSettings.ClampRefreshMinutes(input));
}
