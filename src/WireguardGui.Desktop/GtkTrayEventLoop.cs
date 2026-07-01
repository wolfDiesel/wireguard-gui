namespace WireguardGui.Desktop;

internal sealed class GtkTrayEventLoop : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public void Start()
    {
        if (_task is not null)
            return;

        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts?.Dispose();
    }

    private static async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            GtkBootstrap.PumpEvents(8);
            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
