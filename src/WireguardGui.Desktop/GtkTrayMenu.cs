using System.Runtime.InteropServices;

namespace WireguardGui.Desktop;

internal sealed class GtkTrayMenu : IDisposable
{
    private readonly List<GCHandle> s_handles = [];
    private bool _disposed;

    private GtkTrayMenu(IntPtr menu) => Handle = menu;

    public IntPtr Handle { get; }

    public static GtkTrayMenu? TryCreate(
        TrayMenuLabels labels,
        Action show,
        Action quit,
        Action? connect = null,
        Action? disconnect = null)
    {
        if (!GtkBootstrap.EnsureDisplay())
            return null;

        try
        {
            var menu = gtk_menu_new();
            if (menu == IntPtr.Zero)
                return null;

            GObjectRefSink.TrySink(menu);

            var trayMenu = new GtkTrayMenu(menu);
            trayMenu.AddItem(labels.Show, show);
            if (connect is not null)
                trayMenu.AddItem(labels.Connect, connect);
            if (disconnect is not null)
                trayMenu.AddItem(labels.Disconnect, disconnect);
            trayMenu.AddItem(labels.Quit, quit);
            gtk_widget_show_all(menu);
            return trayMenu;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var handle in s_handles)
        {
            if (handle.IsAllocated)
                handle.Free();
        }

        s_handles.Clear();

        if (Handle != IntPtr.Zero)
            GObjectUnref.TryUnref(Handle);
    }

    private void AddItem(string label, Action action)
    {
        var item = gtk_menu_item_new_with_label(label);
        if (item == IntPtr.Zero)
            return;

        gtk_menu_shell_append(Handle, item);
        var callback = new GtkSignalConnect.WidgetHandlerDelegate((_, _) => action());
        s_handles.Add(GCHandle.Alloc(callback));
        GtkSignalConnect.TryConnectWidget(item, "activate", callback);
    }

    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_menu_new();

    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_menu_item_new_with_label(
        [MarshalAs(UnmanagedType.LPStr)] string label);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_menu_shell_append(IntPtr menu, IntPtr child);

    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_show_all(IntPtr widget);

}

internal static class GObjectRefSink
{
    public static void TrySink(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
            return;

        foreach (var library in new[] { "libgobject-2.0.so.0", "libglib-2.0.so.0" })
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
                continue;

            try
            {
                if (!NativeLibrary.TryGetExport(handle, "g_object_ref_sink", out var symbol))
                    continue;

                var sink = Marshal.GetDelegateForFunctionPointer<RefSinkDelegate>(symbol);
                sink(instance);
                return;
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr RefSinkDelegate(IntPtr instance);
}

internal static class GObjectUnref
{
    public static void TryUnref(IntPtr instance)
    {
        if (instance == IntPtr.Zero)
            return;

        foreach (var library in new[] { "libgobject-2.0.so.0", "libglib-2.0.so.0" })
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
                continue;

            try
            {
                if (!NativeLibrary.TryGetExport(handle, "g_object_unref", out var symbol))
                    continue;

                var unref = Marshal.GetDelegateForFunctionPointer<UnrefDelegate>(symbol);
                unref(instance);
                return;
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UnrefDelegate(IntPtr instance);
}
