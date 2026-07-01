namespace WireguardGui.Desktop;

internal static class GtkDeleteEventHook
{
    private static Func<bool>? s_preventClose;
    private static bool s_allowClose;

    public static void Install(Func<bool> preventClose)
    {
        if (!OperatingSystem.IsLinux())
            return;

        s_preventClose = preventClose;
        if (!GtkBootstrap.EnsureDisplay())
            return;

        foreach (var window in GtkWindowControl.EnumerateWindows(GtkWindowControl.WindowTitle))
            GtkSignalConnect.TryConnectDeleteEvent(window, OnDeleteEvent);
    }

    public static void AllowClose() => s_allowClose = true;

    private static byte OnDeleteEvent(IntPtr widget, IntPtr _, IntPtr __)
    {
        if (s_allowClose)
            return 0;

        if (s_preventClose?.Invoke() != true)
            return 0;

        GtkWindowControl.TryHide();
        return 1;
    }
}
