namespace WireguardGui.Desktop;

public static class CommandLineTorrentLaunch
{
    public static string? FindTorrentPath(string[] args)
    {
        foreach (var arg in args)
        {
            if (IsTorrentPath(arg))
                return Path.GetFullPath(arg);
        }

        return null;
    }

    private static bool IsTorrentPath(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith('-'))
            return false;

        return arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(arg);
    }
}
