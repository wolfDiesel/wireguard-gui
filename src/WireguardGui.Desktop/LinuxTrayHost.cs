namespace WireguardGui.Desktop;

internal static class LinuxTrayHost
{
    public static ILinuxTrayHost? TryCreate(string? iconPath, TrayMenuLabels labels) =>
        AyatanaAppIndicatorTrayHost.TryCreate(iconPath, labels);
}
