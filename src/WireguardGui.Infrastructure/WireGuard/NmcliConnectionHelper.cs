using System.Text.RegularExpressions;
using WireguardGui.Application.Abstractions;

namespace WireguardGui.Infrastructure.WireGuard;

internal static partial class NmcliConnectionHelper
{
    public static async Task<bool> ExistsAsync(
        IProcessRunner processRunner,
        string connectionName,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "nmcli",
            ["connection", "show", connectionName],
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess && !IsUnknownConnection(result);
    }

    public static string? ParseImportedConnectionName(string output)
    {
        var match = ImportNamePattern().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IsInactiveError(ProcessResult result) =>
        ContainsAny(result, "not active", "no active", "нет активного", "не активно");

    public static bool IsUnknownConnection(ProcessResult result) =>
        result.ExitCode != 0 ||
        ContainsAny(
            result,
            "unknown connection",
            "неизвестное соединение",
            "no such connection",
            "не найдено",
            "not found");

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static bool ContainsAny(ProcessResult result, params string[] needles)
    {
        var text = $"{result.StandardError} {result.StandardOutput}";
        return needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"Connection\s+'([^']+)'\s+\(", RegexOptions.IgnoreCase)]
    private static partial Regex ImportNamePattern();
}
