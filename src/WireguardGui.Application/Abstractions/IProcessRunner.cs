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

    bool IsCommandAvailable(string command);
}
