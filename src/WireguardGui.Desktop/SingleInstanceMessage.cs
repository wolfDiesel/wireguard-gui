namespace WireguardGui.Desktop;

internal static class SingleInstanceMessage
{
    public const string ActivateCommand = "ACTIVATE";

    public static string FormatOpenTorrent(string filePath) => $"OPEN:{filePath}";

    public static bool TryParse(string? line, out string? torrentPath, out bool activateOnly)
    {
        torrentPath = null;
        activateOnly = false;

        if (string.IsNullOrWhiteSpace(line))
        {
            activateOnly = true;
            return true;
        }

        var trimmed = line.Trim();
        if (trimmed.Equals(ActivateCommand, StringComparison.Ordinal))
        {
            activateOnly = true;
            return true;
        }

        if (!trimmed.StartsWith("OPEN:", StringComparison.Ordinal))
            return false;

        var path = trimmed["OPEN:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            activateOnly = true;
            return true;
        }

        torrentPath = path;
        activateOnly = false;
        return true;
    }
}
