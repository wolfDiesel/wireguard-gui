using WireguardGui.Infrastructure.SplitRouting.Sources;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class CustomDomainsSplitRouteSourceTests
{
    [Theory]
    [InlineData("instagram.com", new[] { "instagram.com", "www.instagram.com" })]
    [InlineData("www.instagram.com", new[] { "www.instagram.com" })]
    public void ExpandDomainVariants_IncludesWww(string domain, string[] expected) =>
        Assert.Equal(expected, CustomDomainsSplitRouteSource.ExpandDomainVariants(domain).ToArray());

    [Theory]
    [InlineData("157.240.0.0/16", true, "157.240.0.0/16")]
    [InlineData("57.144.244.34/32", true, "57.144.244.34/32")]
    [InlineData("2001:db8::/32", true, "2001:db8::/32")]
    [InlineData("instagram.com", false, "")]
    [InlineData("1.2.3.4/99", false, "")]
    public void TryParseStaticRoute_AcceptsCidrOnly(string entry, bool expected, string route)
    {
        var parsed = CustomDomainsSplitRouteSource.TryParseStaticRoute(entry, out var value);
        Assert.Equal(expected, parsed);
        Assert.Equal(route, value);
    }
}
