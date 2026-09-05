using WireguardGui.App.Avalonia.Services;

namespace WireguardGui.App.Tests;

public class AppVersionServiceTests
{
    [Fact]
    public void Version_Reads_From_Embedded_Resource()
    {
        var service = new AppVersionService();

        Assert.NotNull(service.Version);
        Assert.NotEqual(string.Empty, service.Version);
    }

    [Fact]
    public void Version_Does_Not_Contain_Commit_Suffix()
    {
        var service = new AppVersionService();

        Assert.DoesNotContain("+", service.Version);
        Assert.DoesNotContain("-", service.Version);
    }

    [Fact]
    public void Version_Is_Stable_Across_Calls()
    {
        var service = new AppVersionService();

        Assert.Equal(service.Version, service.Version);
    }
}