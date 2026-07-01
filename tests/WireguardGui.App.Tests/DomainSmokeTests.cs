using WireguardGui.Domain;

namespace WireguardGui.App.Tests;

public class DomainSmokeTests
{
    [Fact]
    public void VpnProfile_Create_HasId()
    {
        var profile = VpnProfile.Create("vpn", BackendKind.Native, "vpn");
        Assert.False(string.IsNullOrWhiteSpace(profile.Id));
    }
}
