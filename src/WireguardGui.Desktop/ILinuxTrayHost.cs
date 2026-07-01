namespace WireguardGui.Desktop;

internal interface ILinuxTrayHost : IDisposable
{
    bool IsActive { get; }

    event Action? ShowRequested;
    event Action? ConnectRequested;
    event Action? DisconnectRequested;
    event Action? QuitRequested;

    void UpdateMenuLabels(TrayMenuLabels labels);

    Task StartAsync(CancellationToken cancellationToken = default);
}
