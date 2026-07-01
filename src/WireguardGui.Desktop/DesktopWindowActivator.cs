using System.Diagnostics;

namespace WireguardGui.Desktop;

internal static class DesktopWindowActivator
{
    public static void TryActivate()
    {
        if (!OperatingSystem.IsLinux())
            return;

        _ = Run("wmctrl", "-x", GtkWindowControl.WindowTitle);
        _ = Run("wmctrl", "-a", GtkWindowControl.WindowTitle);
        _ = Run("wmctrl", "-R", GtkWindowControl.WindowTitle);
    }

    private static int Run(string fileName, params string[] args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            if (!process.Start())
                return 1;

            process.WaitForExit(500);
            return process.ExitCode;
        }
        catch
        {
            return 1;
        }
    }
}
