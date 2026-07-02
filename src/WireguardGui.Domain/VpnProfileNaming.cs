namespace WireguardGui.Domain;

public static class VpnProfileNaming
{
    public static bool IsValidConnectionName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 64
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
