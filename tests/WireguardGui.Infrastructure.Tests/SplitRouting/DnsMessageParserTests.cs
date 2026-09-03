using System.Net;
using System.Text;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.SplitRouting;

namespace WireguardGui.Infrastructure.Tests.SplitRouting;

public class DnsMessageParserTests
{
    [Fact]
    public void TryExtractMatchedHosts_ReturnsARecordsForMatchedQuestion()
    {
        var question = "usher.ttvnw.net";
        var query = BuildQuery(question);
        var response = BuildResponse(question, IPAddress.Parse("1.2.3.4"));

        var ok = DnsMessageParser.TryExtractMatchedHosts(
            query,
            response,
            DnsRouteSuffixes.Twitch,
            out var hosts);

        Assert.True(ok);
        Assert.Equal(["1.2.3.4"], hosts);
    }

    [Fact]
    public void TryExtractMatchedHosts_IgnoresUnmatchedDomain()
    {
        var question = "example.com";
        var query = BuildQuery(question);
        var response = BuildResponse(question, IPAddress.Parse("1.2.3.4"));

        var ok = DnsMessageParser.TryExtractMatchedHosts(
            query,
            response,
            DnsRouteSuffixes.Twitch,
            out _);

        Assert.False(ok);
    }

    private static byte[] BuildQuery(string name)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        WriteName(ms, name);
        ms.Write(new byte[] { 0x00, 0x01, 0x00, 0x01 });
        return ms.ToArray();
    }

    private static byte[] BuildResponse(string name, IPAddress address)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 });
        WriteName(ms, name);
        ms.Write(new byte[] { 0x00, 0x01, 0x00, 0x01 });
        ms.Write(new byte[] { 0xC0, 0x0C });
        ms.Write(new byte[] { 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04 });
        ms.Write(address.GetAddressBytes());
        return ms.ToArray();
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }
}
