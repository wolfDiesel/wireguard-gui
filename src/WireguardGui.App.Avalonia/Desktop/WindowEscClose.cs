using Avalonia.Controls;
using Avalonia.Input;

namespace WireguardGui.App.Avalonia.Desktop;

internal static class WindowEscClose
{
    public static void Attach(Window window, Action? onClose = null)
    {
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            onClose?.Invoke();
            window.Close();
            e.Handled = true;
        };
    }
}
