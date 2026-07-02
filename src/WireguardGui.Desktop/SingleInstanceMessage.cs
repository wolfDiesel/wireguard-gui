namespace WireguardGui.Desktop;

internal static class SingleInstanceMessage
{
    public const string ActivateCommand = "ACTIVATE";

    public static bool IsActivateCommand(string? line) =>
        string.IsNullOrWhiteSpace(line)
        || line.Trim().Equals(ActivateCommand, StringComparison.Ordinal);
}
