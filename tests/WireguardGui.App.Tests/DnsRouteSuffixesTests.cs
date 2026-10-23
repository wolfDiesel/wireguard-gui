using WireguardGui.Domain;

namespace WireguardGui.App.Tests;

public class DnsRouteSuffixesTests
{
    [Fact]
    public void FromSettings_IncludesTwitchAndYouTube()
    {
        var settings = SplitRoutingSettings.CreateDefault() with
        {
            Enabled = true,
            Twitch = true,
            Youtube = true,
            CustomDomains = ["*.example.com", "cdn.test"],
        };

        var suffixes = DnsRouteSuffixes.FromSettings(settings);

        Assert.Contains("ttvnw.net", suffixes);
        Assert.Contains("googlevideo.com", suffixes);
        Assert.Contains("example.com", suffixes);
        Assert.Contains("cdn.test", suffixes);
    }

    [Fact]
    public void Matches_SuffixAndExact()
    {
        var suffixes = new[] { "ttvnw.net" };
        Assert.True(DnsRouteSuffixes.Matches("usher.ttvnw.net", suffixes));
        Assert.True(DnsRouteSuffixes.Matches("ttvnw.net", suffixes));
        Assert.False(DnsRouteSuffixes.Matches("example.com", suffixes));
    }

    [Fact]
    public void FromSettings_Disabled_ReturnsEmpty()
    {
        var settings = SplitRoutingSettings.CreateDefault() with { Enabled = false, Twitch = true };
        Assert.Empty(DnsRouteSuffixes.FromSettings(settings));
    }

    [Fact]
    public void ForTunnelDns_IncludesSplitDomainsAndTelegram()
    {
        var settings = SplitRoutingSettings.CreateDefault() with
        {
            Enabled = true,
            Telegram = true,
            Twitch = true,
            Youtube = false,
            CustomDomains = ["*.example.com"],
        };

        var domains = DnsRouteSuffixes.ForTunnelDns(settings);

        Assert.Contains("example.com", domains);
        Assert.Contains("ttvnw.net", domains);
        Assert.Contains("telegram.org", domains);
        Assert.Contains("t.me", domains);
        Assert.DoesNotContain("googlevideo.com", domains);
    }

    [Fact]
    public void ForTunnelDns_TelegramDisabled_OmitsTelegramDomains()
    {
        var settings = SplitRoutingSettings.CreateDefault() with
        {
            Enabled = true,
            Telegram = false,
            Youtube = false,
            CustomDomains = ["example.com"],
        };

        var domains = DnsRouteSuffixes.ForTunnelDns(settings);

        Assert.Equal(["example.com"], domains);    }

    [Fact]
    public void ForTunnelDns_Disabled_ReturnsEmpty()
    {
        var settings = SplitRoutingSettings.CreateDefault() with { Enabled = false, Telegram = true };
        Assert.Empty(DnsRouteSuffixes.ForTunnelDns(settings));
    }
}
