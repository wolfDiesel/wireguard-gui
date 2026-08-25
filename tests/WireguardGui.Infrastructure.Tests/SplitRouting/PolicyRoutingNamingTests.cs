using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class PolicyRoutingNamingTests
{
    [Fact]
    public void RoutingTableId_IsStableAcrossCalls()
    {
        var first = PolicyRoutingNaming.RoutingTableId("7fdfa871c7404daab73a8e4e94982854");
        var second = PolicyRoutingNaming.RoutingTableId("7fdfa871c7404daab73a8e4e94982854");

        Assert.Equal(first, second);
        Assert.InRange(first, PolicyRoutingNaming.MinRoutingTableId, PolicyRoutingNaming.MinRoutingTableId + PolicyRoutingNaming.RoutingTableIdSpan - 1);
    }

    [Fact]
    public void RoutingTableId_DiffersByProfile()
    {
        var left = PolicyRoutingNaming.RoutingTableId("profile-a");
        var right = PolicyRoutingNaming.RoutingTableId("profile-b");
        Assert.NotEqual(left, right);
    }
}
