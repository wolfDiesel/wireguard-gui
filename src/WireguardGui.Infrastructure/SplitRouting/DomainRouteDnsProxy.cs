using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class DomainRouteDnsProxy(ILogger<DomainRouteDnsProxy> logger) : IDomainRouteDnsProxy, IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private UdpClient? _udp;
    private string? _profileId;
    private IReadOnlyList<string> _suffixes = [];
    private IReadOnlyList<IPEndPoint> _upstreams = [];
    private Func<IReadOnlyList<string>, CancellationToken, Task>? _onResolvedHosts;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _loop is { IsCompleted: false };
        }
    }

    public async Task StartAsync(
        string profileId,
        IReadOnlyList<string> suffixes,
        IReadOnlyList<string> upstreamDns,
        Func<IReadOnlyList<string>, CancellationToken, Task> onResolvedHosts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(onResolvedHosts);

        await StopAsync(cancellationToken).ConfigureAwait(false);

        if (suffixes.Count == 0 || upstreamDns.Count == 0)
            return;

        var upstreams = new List<IPEndPoint>();
        foreach (var server in upstreamDns)
        {
            if (IPAddress.TryParse(server, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                upstreams.Add(new IPEndPoint(ip, 53));
        }

        if (upstreams.Count == 0)
            return;

        var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, IDomainRouteDnsProxy.ListenPort));
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_gate)
        {
            _udp = udp;
            _cts = cts;
            _profileId = profileId;
            _suffixes = suffixes.ToArray();
            _upstreams = upstreams;
            _onResolvedHosts = onResolvedHosts;
            _loop = Task.Run(() => RunAsync(udp, cts.Token), CancellationToken.None);
        }

        logger.LogInformation(
            "DNS route proxy listening on 127.0.0.1:{Port} for {Profile} ({SuffixCount} suffixes)",
            IDomainRouteDnsProxy.ListenPort,
            profileId,
            suffixes.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;
        UdpClient? udp;
        string? profileId;

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            udp = _udp;
            profileId = _profileId;
            _cts = null;
            _loop = null;
            _udp = null;
            _profileId = null;
            _suffixes = [];
            _upstreams = [];
            _onResolvedHosts = null;
        }

        if (cts is null && udp is null)
            return;

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        udp?.Dispose();

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        cts?.Dispose();
        if (profileId is not null)
            logger.LogInformation("DNS route proxy stopped for {Profile}", profileId);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult datagram;
            try
            {
                datagram = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "DNS proxy receive failed");
                continue;
            }

            _ = HandleQueryAsync(udp, datagram, cancellationToken);
        }
    }

    private async Task HandleQueryAsync(
        UdpClient udp,
        UdpReceiveResult datagram,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IPEndPoint> upstreams;
        IReadOnlyList<string> suffixes;
        Func<IReadOnlyList<string>, CancellationToken, Task>? onResolved;

        lock (_gate)
        {
            upstreams = _upstreams;
            suffixes = _suffixes;
            onResolved = _onResolvedHosts;
        }

        if (upstreams.Count == 0 || onResolved is null)
            return;

        byte[]? response = null;
        foreach (var upstream in upstreams)
        {
            response = await ForwardAsync(datagram.Buffer, upstream, cancellationToken).ConfigureAwait(false);
            if (response is not null)
                break;
        }

        if (response is null)
            return;

        try
        {
            await udp.SendAsync(response, datagram.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DNS proxy reply failed");
            return;
        }

        if (!DnsMessageParser.TryExtractMatchedHosts(datagram.Buffer, response, suffixes, out var hosts)
            || hosts.Count == 0)
        {
            return;
        }

        try
        {
            await onResolved(hosts, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("DNS proxy added {Count} hosts for matched query", hosts.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DNS proxy failed to install resolved hosts");
        }
    }

    private static async Task<byte[]?> ForwardAsync(
        byte[] query,
        IPEndPoint upstream,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new UdpClient();
            client.Client.SendTimeout = 3000;
            client.Client.ReceiveTimeout = 3000;
            await client.SendAsync(query, upstream, cancellationToken).ConfigureAwait(false);
            var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            return result.Buffer;
        }
        catch
        {
            return null;
        }
    }
}

internal static class DnsMessageParser
{
    public static bool TryExtractMatchedHosts(
        ReadOnlySpan<byte> query,
        ReadOnlySpan<byte> response,
        IReadOnlyList<string> suffixes,
        out IReadOnlyList<string> hosts)
    {
        hosts = [];
        if (!TryReadQuestionName(query, out var questionHost))
            return false;

        if (!DnsRouteSuffixes.Matches(questionHost, suffixes))
            return false;

        if (!TryReadAddresses(response, out var addresses) || addresses.Count == 0)
            return false;

        hosts = addresses;
        return true;
    }

    private static bool TryReadQuestionName(ReadOnlySpan<byte> message, out string host)
    {
        host = string.Empty;
        if (message.Length < 12)
            return false;

        var qdCount = BinaryPrimitives.ReadUInt16BigEndian(message[4..6]);
        if (qdCount == 0)
            return false;

        var offset = 12;
        if (!TryReadName(message, ref offset, out host))
            return false;

        return !string.IsNullOrWhiteSpace(host);
    }

    private static bool TryReadAddresses(ReadOnlySpan<byte> message, out List<string> addresses)
    {
        addresses = [];
        if (message.Length < 12)
            return false;

        var qdCount = BinaryPrimitives.ReadUInt16BigEndian(message[4..6]);
        var anCount = BinaryPrimitives.ReadUInt16BigEndian(message[6..8]);
        var offset = 12;

        for (var i = 0; i < qdCount; i++)
        {
            if (!TrySkipName(message, ref offset))
                return false;
            offset += 4;
            if (offset > message.Length)
                return false;
        }

        for (var i = 0; i < anCount; i++)
        {
            if (!TrySkipName(message, ref offset))
                return false;
            if (offset + 10 > message.Length)
                return false;

            var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..(offset + 2)]);
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..(offset + 10)]);
            offset += 10;
            if (offset + dataLength > message.Length)
                return false;

            if (type == 1 && dataLength == 4)
            {
                var ip = new IPAddress(message.Slice(offset, 4));
                addresses.Add(ip.ToString());
            }

            offset += dataLength;
        }

        return addresses.Count > 0;
    }

    private static bool TryReadName(ReadOnlySpan<byte> message, ref int offset, out string name)
    {
        name = string.Empty;
        var builder = new StringBuilder();
        var jumped = false;
        var hop = 0;
        var endOffset = offset;

        while (offset < message.Length && hop++ < 64)
        {
            var label = message[offset];
            if (label == 0)
            {
                offset++;
                if (!jumped)
                    endOffset = offset;
                break;
            }

            if ((label & 0xC0) == 0xC0)
            {
                if (offset + 1 >= message.Length)
                    return false;
                var pointer = ((label & 0x3F) << 8) | message[offset + 1];
                if (!jumped)
                    endOffset = offset + 2;
                offset = pointer;
                jumped = true;
                continue;
            }

            offset++;
            if (offset + label > message.Length)
                return false;

            if (builder.Length > 0)
                builder.Append('.');
            builder.Append(Encoding.ASCII.GetString(message.Slice(offset, label)));
            offset += label;
            if (!jumped)
                endOffset = offset;
        }

        offset = endOffset;
        name = builder.ToString();
        return name.Length > 0;
    }

    private static bool TrySkipName(ReadOnlySpan<byte> message, ref int offset)
    {
        var hop = 0;
        while (offset < message.Length && hop++ < 64)
        {
            var label = message[offset];
            if (label == 0)
            {
                offset++;
                return true;
            }

            if ((label & 0xC0) == 0xC0)
            {
                offset += 2;
                return offset <= message.Length;
            }

            offset += 1 + label;
        }

        return false;
    }
}
