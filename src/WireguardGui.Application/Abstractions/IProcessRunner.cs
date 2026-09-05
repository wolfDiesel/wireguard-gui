namespace WireguardGui.Application.Abstractions;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> RunPrivilegedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<ProcessResult> RunPrivilegedShellAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True, когда привилегированная (pkexec) сессия уже авторизована и жива.
    /// Позволяет выполнять дешёвые привилегированные пробы (например, wg show)
    /// без нового запроса пароля; при отсутствии сессии — использовать fallback.
    /// </summary>
    bool HasActivePrivilegedSession { get; }

    bool IsCommandAvailable(string command);
}
