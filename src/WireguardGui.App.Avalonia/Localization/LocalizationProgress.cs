using WireguardGui.Application.Contracts;

namespace WireguardGui.App.Avalonia.Localization;

internal static class LocalizationProgress
{
    public static string Format(LocalizationService localization, SplitRoutingProgress progress) =>
        progress.Args.Length == 0
            ? localization.Get(progress.Key)
            : localization.Format(progress.Key, progress.Args);

    public static string Format(LocalizationService localization, string status)
    {
        var pipe = status.IndexOf('|');
        if (pipe < 0)
            return localization.Get(status);

        var key = status[..pipe];
        var args = status[(pipe + 1)..].Split('|');
        return localization.Format(key, args);
    }
}
