using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WireguardGui.Infrastructure.SplitRouting;

internal static class SystemdResolvedMonitorClient
{
    public const string MonitorSocketPath = "/run/systemd/resolve/io.systemd.Resolve.Monitor";

    private const string SubscribeMethod = "io.systemd.Resolve.Monitor.SubscribeQueryResults";
    private const uint RrA = 1;
    private const uint RrAaaa = 28;

    public static async Task SubscribeAsync(
        Func<string, IReadOnlyList<IPAddress>, CancellationToken, Task> onResolved,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(MonitorSocketPath) && !IsSocketPresent(MonitorSocketPath))
            throw new InvalidOperationException($"Missing resolved monitor socket: {MonitorSocketPath}");

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(MonitorSocketPath), cancellationToken)
            .ConfigureAwait(false);

        await SendSubscribeAsync(socket, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[65536];
        var pending = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("resolved monitor connection closed");

            pending.Write(buffer, 0, read);
            while (TryTakeFrame(pending, out var frame))
            {
                if (frame.Length == 0)
                    continue;

                await HandleFrameAsync(frame, onResolved, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task HandleFrameAsync(
        byte[] frame,
        Func<string, IReadOnlyList<IPAddress>, CancellationToken, Task> onResolved,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            var error = errorElement.GetString() ?? "unknown";
            throw new InvalidOperationException($"resolved monitor error: {error}");
        }

        if (!root.TryGetProperty("parameters", out var parameters))
            return;

        if (parameters.TryGetProperty("ready", out _))
            return;

        if (!TryParseQueryResult(parameters, out var host, out var addresses))
            return;

        await onResolved(host, addresses, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendSubscribeAsync(
        Socket socket,
        CancellationToken cancellationToken)
    {
        var payload =
            "{\"method\":\"" + SubscribeMethod +
            "\",\"parameters\":{},\"more\":true}\0";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes.AsMemory(), SocketFlags.None, cancellationToken).ConfigureAwait(false);
    }

    internal static bool TryParseQueryResult(
        JsonElement parameters,
        out string host,
        out IReadOnlyList<IPAddress> addresses)
    {
        host = string.Empty;
        addresses = [];

        if (parameters.TryGetProperty("state", out var stateElement))
        {
            var state = stateElement.GetString();
            if (!string.Equals(state, "success", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!parameters.TryGetProperty("question", out var question) ||
            question.ValueKind != JsonValueKind.Array ||
            question.GetArrayLength() == 0)
        {
            return false;
        }

        var firstQuestion = question[0];
        if (!firstQuestion.TryGetProperty("name", out var nameElement))
            return false;

        host = nameElement.GetString()?.Trim().TrimEnd('.') ?? string.Empty;
        if (host.Length == 0)
            return false;

        if (!parameters.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.Array)
            return false;

        var parsed = new List<IPAddress>();
        foreach (var entry in answer.EnumerateArray())
        {
            if (!entry.TryGetProperty("rr", out var rr))
                continue;
            if (!rr.TryGetProperty("key", out var key) || !key.TryGetProperty("type", out var typeElement))
                continue;
            if (!rr.TryGetProperty("address", out var addressElement) ||
                addressElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (!typeElement.TryGetUInt32(out var type))
                continue;

            var bytes = new byte[addressElement.GetArrayLength()];
            var i = 0;
            foreach (var b in addressElement.EnumerateArray())
            {
                if (!b.TryGetByte(out var value))
                {
                    bytes = [];
                    break;
                }

                bytes[i++] = value;
            }

            if (type == RrA && bytes.Length == 4)
                parsed.Add(new IPAddress(bytes));
            else if (type == RrAaaa && bytes.Length == 16)
                parsed.Add(new IPAddress(bytes));
        }

        if (parsed.Count == 0)
            return false;

        addresses = parsed;
        return true;
    }

    private static bool TryTakeFrame(MemoryStream pending, out byte[] frame)
    {
        frame = [];
        if (!pending.TryGetBuffer(out var segment))
        {
            var copy = pending.ToArray();
            segment = new ArraySegment<byte>(copy);
        }

        var data = segment.Array!;
        var offset = segment.Offset;
        var length = segment.Count;
        var nulRelative = Array.IndexOf(data, (byte)0, offset, length);
        if (nulRelative < 0)
            return false;

        var nul = nulRelative - offset;
        frame = new byte[nul];
        Buffer.BlockCopy(data, offset, frame, 0, nul);
        var remaining = length - nul - 1;
        pending.SetLength(0);
        if (remaining > 0)
            pending.Write(data, offset + nul + 1, remaining);
        return true;
    }

    private static bool IsSocketPresent(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                   || File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
