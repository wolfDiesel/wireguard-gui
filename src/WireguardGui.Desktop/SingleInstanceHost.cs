using System.Net.Sockets;
using System.Text;

namespace WireguardGui.Desktop;

public sealed class SingleInstanceHost : IAsyncDisposable
{
    private readonly Action _activateWindow;
    private readonly string _socketPath;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public SingleInstanceHost(Action activateWindow, string? socketPath = null)
    {
        _activateWindow = activateWindow;
        _socketPath = socketPath ?? ResolveSocketPath();
    }

    public static bool TryForwardToRunningInstance(string? socketPath = null)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        var path = socketPath ?? ResolveSocketPath();
        if (!File.Exists(path))
            return false;

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(path));
            var bytes = Encoding.UTF8.GetBytes(SingleInstanceMessage.ActivateCommand);
            client.Send(bytes);
            return true;
        }
        catch (SocketException)
        {
            TryDeleteStaleSocket(path);
            return false;
        }
    }

    private static void TryDeleteStaleSocket(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public void Start()
    {
        if (!OperatingSystem.IsLinux())
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_socketPath)!);
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(5);

        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        _listener?.Dispose();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (File.Exists(_socketPath))
            File.Delete(_socketPath);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void HandleClientAsync(Socket client)
    {
        using (client)
        {
            var buffer = new byte[4096];
            var received = client.Receive(buffer);
            if (received <= 0)
                return;

            var line = Encoding.UTF8.GetString(buffer, 0, received);
            if (!SingleInstanceMessage.IsActivateCommand(line))
                return;

            _activateWindow();
        }
    }

    internal static string ResolveSocketPath()
    {
        var custom = Environment.GetEnvironmentVariable("WIREGUARDGUI_INSTANCE_SOCKET");
        if (!string.IsNullOrWhiteSpace(custom))
            return custom;

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var baseDir = !string.IsNullOrWhiteSpace(runtimeDir) ? runtimeDir : Path.GetTempPath();
        return Path.Combine(baseDir, "wireguard-gui.sock");
    }
}
