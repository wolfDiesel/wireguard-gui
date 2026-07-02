using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Infrastructure.System;

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner, IAsyncDisposable
{
    private PrivilegedShellSession? _privilegedSession;

    public bool IsCommandAvailable(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate) && IsExecutable(candidate))
                return true;
        }

        return false;
    }

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(fileName, arguments, cancellationToken, CommandTimeout);

    public Task<ProcessResult> RunPrivilegedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!NeedsPrivilegeElevation())
            return RunCoreAsync(fileName, arguments, cancellationToken, timeout: null);

        return RunPrivilegedShellAsync(BuildShellCommand(fileName, arguments), cancellationToken);
    }

    public async Task<ProcessResult> RunPrivilegedShellAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        if (!NeedsPrivilegeElevation())
            return await RunCoreAsync("bash", ["-c", script], cancellationToken, timeout: null)
                .ConfigureAwait(false);

        logger.LogInformation("Privileged command: {Script}", script);
        var session = GetPrivilegedSession();
        return await session.ExecuteAsync(script, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_privilegedSession is not null)
            await _privilegedSession.DisposeAsync().ConfigureAwait(false);
    }

    private PrivilegedShellSession GetPrivilegedSession() =>
        _privilegedSession ??= new PrivilegedShellSession(logger);

    private static bool NeedsPrivilegeElevation() =>
        OperatingSystem.IsLinux() && GetEffectiveUserId() != 0;

    private static string BuildShellCommand(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(ShellQuote(fileName));
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(ShellQuote(argument));
        }

        return builder.ToString();
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private async Task<ProcessResult> RunCoreAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start {FileName}", fileName);
            throw new WireGuardOperationException("Failed to start command", ex.Message);
        }

        logger.LogDebug("Starting: {FileName} {Arguments}", fileName, FormatArgs(arguments));

        using var timeoutCts = timeout is { } value
            ? new CancellationTokenSource(value)
            : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var waitToken = linkedCts?.Token ?? cancellationToken;

        var stdoutTask = process.StandardOutput.ReadToEndAsync(waitToken);
        var stderrTask = process.StandardError.ReadToEndAsync(waitToken);
        try
        {
            await process.WaitForExitAsync(waitToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return new ProcessResult(-1, string.Empty, "Command timed out");
        }

        var result = new ProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));

        if (!result.IsSuccess)
            logger.LogWarning(
                "Command exited with code {ExitCode}: {FileName} {Arguments} — {Error}",
                result.ExitCode,
                fileName,
                FormatArgs(arguments),
                string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
        else
            logger.LogDebug("Command completed successfully: {FileName}", fileName);

        return result;
    }

    private static string FormatArgs(IReadOnlyList<string> arguments) =>
        arguments.Count == 0 ? string.Empty : string.Join(' ', arguments);

    private static bool IsExecutable(string path)
    {
        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & UnixFileMode.UserExecute) != 0
                   || (mode & UnixFileMode.GroupExecute) != 0
                   || (mode & UnixFileMode.OtherExecute) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static uint GetEffectiveUserId()
    {
        if (!OperatingSystem.IsLinux())
            return 1;

        return geteuid();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();
}
