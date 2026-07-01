using System.Runtime.InteropServices;

namespace WireguardGui.Desktop;

internal static class GtkWindowControl
{
    public const string WindowTitle = "WireGuard GUI";

    public static bool TryHide() => SetVisible(false);

    public static bool TryShow()
    {
        if (!SetVisible(true))
            return false;

        GtkBootstrap.PumpEvents();
        return true;
    }

    private static bool SetVisible(bool visible)
    {
        if (!GtkBootstrap.EnsureDisplay())
            return false;

        var changed = false;
        foreach (var window in EnumerateWindows(WindowTitle))
        {
            if (visible)
                gtk_widget_show(window);
            else
                gtk_widget_hide(window);

            changed = true;
        }

        return changed;
    }

    internal static IEnumerable<IntPtr> EnumerateWindows(string title)
    {
        var list = gtk_window_list_toplevels();
        if (list == IntPtr.Zero)
            yield break;

        for (var node = list; node != IntPtr.Zero; node = ReadGListNext(node))
        {
            var data = Marshal.ReadIntPtr(node);
            if (data == IntPtr.Zero || !TitleMatches(data, title))
                continue;

            yield return data;
        }
    }

    private static IntPtr ReadGListNext(IntPtr node) =>
        Marshal.ReadIntPtr(node + IntPtr.Size);

    private static bool TitleMatches(IntPtr window, string title)
    {
        var ptr = gtk_window_get_title(window);
        if (ptr == IntPtr.Zero)
            return false;

        var value = Marshal.PtrToStringUTF8(ptr);
        return string.Equals(value, title, StringComparison.Ordinal);
    }

    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_window_list_toplevels();

    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_window_get_title(IntPtr window);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_hide(IntPtr widget);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_show(IntPtr widget);

}
