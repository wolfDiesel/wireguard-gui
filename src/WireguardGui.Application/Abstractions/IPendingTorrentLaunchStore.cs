namespace WireguardGui.Application.Abstractions;

public interface IPendingTorrentLaunchStore
{
    void SetPendingPath(string path);

    string? TakePendingPath();
}
