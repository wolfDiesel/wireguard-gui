using System.Runtime.InteropServices;

namespace WireguardGui.Desktop;

internal static class GtkSignalConnect
{
    private static readonly List<GCHandle> s_handles = [];
    private static g_signal_connect_data_delegate? s_connect;

    public static bool TryConnect(IntPtr instance, string signal, Action handler)
    {
        var callback = new WidgetHandlerDelegate((_, _) => handler());
        return TryConnectWidget(instance, signal, callback);
    }

    public static bool TryConnectWidget(IntPtr instance, string signal, WidgetHandlerDelegate handler)
    {
        if (!EnsureResolved())
            return false;

        s_handles.Add(GCHandle.Alloc(handler));
        s_connect!(instance, signal, handler, IntPtr.Zero, IntPtr.Zero, 0);
        return true;
    }

    public static bool TryConnectPopupMenu(IntPtr statusIcon, PopupMenuHandlerDelegate handler)
    {
        if (!EnsureResolved())
            return false;

        s_handles.Add(GCHandle.Alloc(handler));
        s_connect!(statusIcon, "popup-menu", handler, IntPtr.Zero, IntPtr.Zero, 0);
        return true;
    }

    public static bool TryConnectDeleteEvent(IntPtr window, DeleteEventHandlerDelegate handler)
    {
        if (!EnsureResolved())
            return false;

        s_handles.Add(GCHandle.Alloc(handler));
        s_connect!(window, "delete-event", handler, IntPtr.Zero, IntPtr.Zero, 0);
        return true;
    }

    private static bool EnsureResolved()
    {
        if (s_connect is not null)
            return true;

        foreach (var library in new[] { "libgobject-2.0.so.0", "libgobject-2.0.so", "libgtk-3.so.0" })
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
                continue;

            try
            {
                if (NativeLibrary.TryGetExport(handle, "g_signal_connect_data", out var symbol))
                {
                    s_connect = Marshal.GetDelegateForFunctionPointer<g_signal_connect_data_delegate>(symbol);
                    return true;
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }

        return false;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WidgetHandlerDelegate(IntPtr widget, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PopupMenuHandlerDelegate(IntPtr statusIcon, uint button, uint activateTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate byte DeleteEventHandlerDelegate(IntPtr widget, IntPtr gdkEvent, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong g_signal_connect_data_delegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPStr)] string detailedSignal,
        Delegate callback,
        IntPtr userData,
        IntPtr destroyData,
        int connectFlags);
}
