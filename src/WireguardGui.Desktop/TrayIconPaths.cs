namespace WireguardGui.Desktop;

internal static class TrayIconPaths
{
    public const string TrayPngFileName = "wireguard-gui-tray.png";
    public const string AppPngFileName = "wireguard-gui.png";

    public static string? ResolveTrayIconPath()
    {
        foreach (var path in CandidatePaths(TrayPngFileName, AppPngFileName))
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths(params string[] fileNames)
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var fileName in fileNames)
            yield return Path.Combine(baseDir, fileName);
    }
}
