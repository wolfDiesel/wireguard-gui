using System.Runtime.InteropServices;

namespace WireguardGui.Desktop;

internal static class GtkBootstrap
{
    private static bool s_initialized;

    public static bool EnsureDisplay()
    {
        if (s_initialized)
            return true;

        if (gdk_display_get_default() != IntPtr.Zero)
        {
            s_initialized = true;
            return true;
        }

        if (!NativeLibrary.TryLoad("libgtk-3.so.0", out var gtk))
            return false;

        try
        {
            var init = GetDelegate<gtk_init_check_delegate>(gtk, "gtk_init_check");
            init(0, IntPtr.Zero);
            s_initialized = gdk_display_get_default() != IntPtr.Zero;
            return s_initialized;
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeLibrary.Free(gtk);
        }
    }

    public static void PumpEvents(int iterations = 16)
    {
        if (!s_initialized)
            return;

        PumpGlib(iterations);
        PumpGtk(iterations);
    }

    private static void PumpGtk(int iterations)
    {
        if (!NativeLibrary.TryLoad("libgtk-3.so.0", out var gtk))
            return;

        try
        {
            var iterate = GetDelegate<gtk_main_iteration_delegate>(gtk, "gtk_main_iteration");
            var pending = GetDelegate<gtk_events_pending_delegate>(gtk, "gtk_events_pending");
            for (var i = 0; i < iterations; i++)
            {
                if (pending() != 0)
                    iterate();
            }
        }
        catch
        {
        }
        finally
        {
            NativeLibrary.Free(gtk);
        }
    }

    private static void PumpGlib(int iterations)
    {
        if (!NativeLibrary.TryLoad("libglib-2.0.so.0", out var glib))
            return;

        try
        {
            var contextDefault = GetDelegate<g_main_context_default_delegate>(glib, "g_main_context_default");
            var contextPending = GetDelegate<g_main_context_pending_delegate>(glib, "g_main_context_pending");
            var contextIteration = GetDelegate<g_main_context_iteration_delegate>(glib, "g_main_context_iteration");
            var ctx = contextDefault();
            for (var i = 0; i < iterations; i++)
            {
                while (contextPending(ctx) != 0)
                    contextIteration(ctx, 0);
            }
        }
        catch
        {
        }
        finally
        {
            NativeLibrary.Free(glib);
        }
    }

    private static T GetDelegate<T>(IntPtr library, string name)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [DllImport("libgdk-3.so.0")]
    private static extern IntPtr gdk_display_get_default();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void gtk_init_check_delegate(int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int gtk_events_pending_delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void gtk_main_iteration_delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr g_main_context_default_delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int g_main_context_pending_delegate(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int g_main_context_iteration_delegate(IntPtr context, int mayBlock);
}
