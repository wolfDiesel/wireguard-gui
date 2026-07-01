using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure.Tests.WireGuard;

public class WireGuardConfigValidatorTests
{
    private readonly WireGuardConfigValidator _validator = new();

    [Fact]
    public void Validate_Throws_WhenMissingInterface()
    {
        var ex = Assert.Throws<WireGuardConfigValidationException>(() =>
            _validator.Validate("[Peer]\nPublicKey = abc\n"));
        Assert.Contains("Interface", ex.Message);
    }

    [Fact]
    public void Validate_Passes_ForMinimalConfig()
    {
        var config = """
            [Interface]
            PrivateKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            [Peer]
            PublicKey = abcdefghijklmnopqrstuvwxyz0123456789ABCD=
            Endpoint = 1.2.3.4:51820
            """;
        _validator.Validate(config);
    }
}

public class WireGuardConfigParserTests
{
    private readonly WireGuardConfigParser _parser = new();

    [Fact]
    public void WriteAllowedIps_ReplacesExisting()
    {
        var config = """
            [Interface]
            PrivateKey = x
            [Peer]
            PublicKey = y
            AllowedIPs = 10.0.0.0/8
            """;
        var updated = _parser.WriteAllowedIps(config, "1.1.1.1/32,2.2.2.2/32");
        Assert.Contains("AllowedIPs = 1.1.1.1/32,2.2.2.2/32", updated);
        Assert.DoesNotContain("10.0.0.0/8", updated);
    }

    [Fact]
    public void ReadInterfaceName_ReturnsValue()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Name = wg0
            [Peer]
            PublicKey = y
            """;
        Assert.Equal("wg0", _parser.ReadInterfaceName(config));
    }

    [Fact]
    public void RemoveInterfaceName_RemovesNameLine()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Name = wireguard
            [Peer]
            PublicKey = y
            """;
        var updated = _parser.RemoveInterfaceName(config);
        Assert.DoesNotContain("Name =", updated);
        Assert.Contains("[Interface]", updated);
    }

    [Fact]
    public void RemoveDns_RemovesInterfaceDnsLine()
    {
        var config = """
            [Interface]
            PrivateKey = x
            DNS = 1.1.1.1
            [Peer]
            PublicKey = y
            """;
        var updated = _parser.RemoveDns(config);
        Assert.DoesNotContain("DNS", updated);
        Assert.Contains("[Interface]", updated);
    }

    [Fact]
    public void NormalizeAllowedIps_SortsAndTrims()
    {
        Assert.Equal("1.1.1.1/32,2.2.2.2/32", _parser.NormalizeAllowedIps(" 2.2.2.2/32,1.1.1.1/32 "));
    }

    [Fact]
    public void ReadAllowedIps_ReturnsValue()
    {
        var config = "[Peer]\nAllowedIPs = 1.2.3.4/32\n";
        Assert.Equal("1.2.3.4/32", _parser.ReadAllowedIps(config));
    }
}
