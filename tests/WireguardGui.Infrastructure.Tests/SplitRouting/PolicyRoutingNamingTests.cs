using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class PolicyRoutingNamingTests
{
    [Fact]
    public void ClassifyRoutes_SplitsHostsAndNets()
    {
        var (hosts, nets, hosts6) = PolicyRoutingNaming.ClassifyRoutes(
            ["1.1.1.1/32", "149.154.160.0/20", "93.184.216.34", "2001:db8::1/128"]);

        Assert.Equal(["1.1.1.1", "93.184.216.34"], hosts);
        Assert.Equal(["149.154.160.0/20"], nets);
        Assert.Equal(["2001:db8::1"], hosts6);
    }

    [Fact]
    public void Sanitize_ReplacesInvalidCharacters()
    {
        Assert.Equal("abc_def", PolicyRoutingNaming.Sanitize("abc-def"));
    }

    [Fact]
    public void FormatNftElements_EmptySet()
    {
        Assert.Equal("{ }", PolicyRoutingNaming.FormatNftElements([]));
    }
}
