namespace WireguardGui.App.Avalonia.Services;

internal sealed class DesktopSessionBridge
{
    private AvaloniaDesktopSession? _session;

    public void Attach(AvaloniaDesktopSession session) => _session = session;

    public void UpdateTrayLabels() => _session?.UpdateTrayLabels();
}
