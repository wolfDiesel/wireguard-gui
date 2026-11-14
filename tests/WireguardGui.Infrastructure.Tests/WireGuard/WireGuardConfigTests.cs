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

    [Fact]
    public void EnsurePolicySplitBaseline_SetsTableOffAndWideAllowedIps()
    {
        var config = """
            [Interface]
            PrivateKey = x
            DNS = 1.1.1.1
            [Peer]
            PublicKey = y
            AllowedIPs = 10.0.0.0/8
            Endpoint = vpn.example.com:51820
            """;
        var updated = _parser.EnsurePolicySplitBaseline(config);
        Assert.Contains("Table = off", updated);
        Assert.Contains("AllowedIPs = 0.0.0.0/0", updated);
        Assert.Contains("DNS = 1.1.1.1", updated);
        Assert.True(_parser.IsPolicySplitBaseline(updated));
    }

    [Fact]
    public void EnsurePolicySplitBaseline_UsesDefaultDnsWhenMissing()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Address = 10.8.0.5/32
            [Peer]
            PublicKey = y
            AllowedIPs = 10.0.0.0/8
            """;
        var updated = _parser.EnsurePolicySplitBaseline(config);
        Assert.Contains("DNS = 8.8.8.8", updated);
    }

    [Fact]
    public void ReadDnsServers_ParsesCommaSeparatedValues()
    {
        var config = """
            [Interface]
            PrivateKey = x
            DNS = 10.8.0.1, 1.1.1.1
            [Peer]
            PublicKey = y
            """;
        Assert.Equal(["10.8.0.1", "1.1.1.1"], _parser.ReadDnsServers(config));
        Assert.Equal(["10.8.0.1", "1.1.1.1"], _parser.ResolveTunnelDnsServers(config));
    }

    [Fact]
    public void ApplyTunnelDns_WritesAndRemovesDns()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Address = 10.8.0.5/32
            [Peer]
            PublicKey = y
            """;
        var withDns = _parser.ApplyTunnelDns(config, "10.8.0.1");
        Assert.Contains("DNS = 10.8.0.1", withDns);

        var withoutDns = _parser.ApplyTunnelDns(withDns, null);
        Assert.DoesNotContain("DNS =", withoutDns);
    }

    [Fact]
    public void WriteDns_PreservesFollowingLinesWhenUpdating()
    {
        var config = """
            [Interface]
            DNS = 10.8.0.1
            PrivateKey = keep-me
            Address = 10.8.0.5/32
            Table = off
            [Peer]
            PublicKey = y
            AllowedIPs = 0.0.0.0/0
            Endpoint = 1.2.3.4:51820
            """;
        var updated = _parser.ApplyTunnelDns(config, "8.8.8.8");
        Assert.Contains("DNS = 8.8.8.8\n", updated.Replace("\r\n", "\n"));
        Assert.Contains("PrivateKey = keep-me", updated);
        Assert.Contains("Endpoint = 1.2.3.4:51820", updated);
        Assert.DoesNotContain("8.8.8.8PrivateKey", updated);
        Assert.DoesNotContain("8.8.8.8Endpoint", updated);

        var again = _parser.ApplyTunnelDns(updated, "1.1.1.1");
        Assert.Contains("PrivateKey = keep-me", again);
        Assert.Contains("Endpoint = 1.2.3.4:51820", again);
    }

    [Fact]
    public void ReadEndpointHost_StripsPortAndBrackets()
    {
        var config = """
            [Peer]
            PublicKey = y
            Endpoint = [2001:db8::1]:51820
            """;
        Assert.Equal("2001:db8::1", _parser.ReadEndpointHost(config));
    }

    [Fact]
    public void IsAmneziaWg_DetectsObfuscationKeys()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Jc = 4
            Jmin = 50
            Jmax = 1000
            S1 = 20
            S2 = 20
            H1 = 123456
            [Peer]
            PublicKey = y
            """;
        Assert.True(_parser.IsAmneziaWg(config));
    }

    [Fact]
    public void IsAmneziaWg_DetectsAwg3Keys()
    {
        var config = """
            [Interface]
            PrivateKey = x
            S3 = 30
            S4 = 30
            H1 = 100-200
            I1 = <b 0x0102><r 16>
            HeaderProtectionKey = abc=
            ContentPaddingAddition = 16
            [Peer]
            PublicKey = y
            """;
        Assert.True(_parser.IsAmneziaWg(config));
    }

    [Fact]
    public void IsAmneziaWg_DetectsKeysCaseInsensitively()
    {
        var config = """
            [Interface]
            PrivateKey = x
            jc = 4
            h4 = 50000
            [Peer]
            PublicKey = y
            """;
        Assert.True(_parser.IsAmneziaWg(config));
    }

    [Fact]
    public void IsAmneziaWg_ReturnsFalse_ForPlainWireGuard()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Address = 10.8.0.5/32
            DNS = 10.8.0.1
            [Peer]
            PublicKey = y
            AllowedIPs = 0.0.0.0/0
            Endpoint = 1.2.3.4:51820
            """;
        Assert.False(_parser.IsAmneziaWg(config));
    }

    [Fact]
    public void EnsurePolicySplitBaseline_PreservesAmneziaWgKeys()
    {
        var config = """
            [Interface]
            PrivateKey = x
            Jc = 4
            Jmin = 50
            Jmax = 1000
            S1 = 20
            S2 = 20
            H1 = 123456
            [Peer]
            PublicKey = y
            AllowedIPs = 10.0.0.0/8
            """;
        var updated = _parser.EnsurePolicySplitBaseline(config);

        Assert.Contains("Table = off", updated);
        Assert.Contains("AllowedIPs = 0.0.0.0/0", updated);
        Assert.Contains("Jc = 4", updated);
        Assert.Contains("H1 = 123456", updated);

        var rewritten = _parser.WriteAllowedIps(updated, "1.1.1.1/32,2.2.2.2/32");
        Assert.Contains("Jc = 4", rewritten);
        Assert.Contains("S2 = 20", rewritten);
        Assert.True(_parser.IsAmneziaWg(rewritten));
    }
}
