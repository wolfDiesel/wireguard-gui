using WireguardGui.Domain;

namespace WireguardGui.App.Tests;

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
}
