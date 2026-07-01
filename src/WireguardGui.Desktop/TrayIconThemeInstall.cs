namespace WireguardGui.Desktop;

internal static class TrayIconThemeInstall
{
    public const string IconName = "wireguard-gui";

    public static string? TryInstall(string? pngPath)
    {
        if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath))
            return null;

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var size in new[] { 16, 22, 24, 32, 48 })
            {
                var dir = Path.Combine(home, ".local/share/icons/hicolor", $"{size}x{size}", "apps");
                Directory.CreateDirectory(dir);
                File.Copy(pngPath, Path.Combine(dir, $"{IconName}.png"), overwrite: true);
            }

            return IconName;
        }
        catch
        {
            return null;
        }
    }
}
