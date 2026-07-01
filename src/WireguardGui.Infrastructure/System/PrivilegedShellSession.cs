using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Infrastructure.System;

internal sealed class PrivilegedShellSession(ILogger logger) : IAsyncDisposable
{
    private const string HelperScript = """
        #!/bin/bash
        while IFS= read -r encoded; do
          [ -z "$encoded" ] && continue
          script=$(echo "$encoded" | base64 -d) || {
            echo "__WG_GUI_STDERR__"
            echo "decode failed"
            echo "__WG_GUI_EXIT__1"
            continue
          }
          tmpout=$(mktemp)
          tmperr=$(mktemp)
          bash -c "$script" >"$tmpout" 2>"$tmperr"
          code=$?
          echo "__WG_GUI_STDOUT__"
          cat "$tmpout"
          echo "__WG_GUI_STDERR__"
          cat "$tmperr"
          echo "__WG_GUI_EXIT__${code}"
          rm -f "$tmpout" "$tmperr"
        done
        """;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private string? _helperPath;

    public async Task<ProcessResult> ExecuteAsync(string script, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            await _stdin!.WriteLineAsync(encoded.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            return await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Привилегированная сессия сброшена");
            await ResetAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
            return;

        await ResetAsync().ConfigureAwait(false);

        var helperPath = GetHelperPath();
        await File.WriteAllTextAsync(helperPath, HelperScript, cancellationToken).ConfigureAwait(false);
        File.SetUnixFileMode(helperPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        if (!IsCommandAvailable("pkexec"))
            throw new PrivilegeRequiredException("pkexec не найден — нужны права администратора");

        logger.LogInformation("Запуск привилегированной сессии (один запрос пароля на время работы приложения)");

        var psi = new ProcessStartInfo
        {
            FileName = "pkexec",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(helperPath);

        _process = new Process { StartInfo = psi };
        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            throw new WireGuardOperationException("Не удалось запустить привилегированную сессию", ex.Message);
        }

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(StartTimeout);
        try
        {
            await _stdin.WriteLineAsync(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("true")).AsMemory(),
                timeoutCts.Token).ConfigureAwait(false);
            await _stdin.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            var ping = await ReadResponseAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!ping.IsSuccess)
                throw new WireGuardOperationException("Привилегированная сессия не отвечает", ping.StandardError.Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ResetAsync().ConfigureAwait(false);
            throw new WireGuardOperationException("Таймаут ожидания авторизации pkexec", null);
        }

        if (_process.HasExited)
        {
            var err = await _process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new WireGuardOperationException(
                "Не удалось открыть привилегированную сессию",
                err.Trim());
        }
    }

    private async Task<ProcessResult> ReadResponseAsync(CancellationToken cancellationToken)
    {
        if (_stdout is null || _process is null)
            throw new InvalidOperationException("Привилегированная сессия не запущена");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var section = Section.None;

        while (true)
        {
            var line = await _stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (_process.HasExited)
                    break;
                throw new WireGuardOperationException("Привилегированная сессия оборвалась", null);
            }

            if (line.StartsWith("__WG_GUI_EXIT__", StringComparison.Ordinal))
            {
                var codeText = line["__WG_GUI_EXIT__".Length..];
                var exitCode = int.TryParse(codeText, out var code) ? code : 1;
                var result = new ProcessResult(exitCode, stdout.ToString(), stderr.ToString());
                if (IsPrivilegeDenied(result))
                    throw new PrivilegeRequiredException();
                return result;
            }

            switch (line)
            {
                case "__WG_GUI_STDOUT__":
                    section = Section.Stdout;
                    continue;
                case "__WG_GUI_STDERR__":
                    section = Section.Stderr;
                    continue;
            }

            switch (section)
            {
                case Section.Stdout:
                    if (stdout.Length > 0)
                        stdout.AppendLine();
                    stdout.Append(line);
                    break;
                case Section.Stderr:
                    if (stderr.Length > 0)
                        stderr.AppendLine();
                    stderr.Append(line);
                    break;
            }
        }

        throw new WireGuardOperationException("Привилегированная сессия завершилась без ответа", null);
    }

    private async Task ResetAsync()
    {
        _stdin = null;
        _stdout = null;

        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private string GetHelperPath()
    {
        if (_helperPath is not null)
            return _helperPath;

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "wireguard-gui");
        Directory.CreateDirectory(root);
        _helperPath = Path.Combine(root, "privileged-shell.sh");
        return _helperPath;
    }

    private static bool IsCommandAvailable(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, command)))
                return true;
        }

        return false;
    }

    private static bool IsPrivilegeDenied(ProcessResult result)
    {
        if (result.ExitCode == 126 || result.ExitCode == 127)
            return true;

        var combined = $"{result.StandardError} {result.StandardOutput}".ToLowerInvariant();
        return combined.Contains("not authorized")
               || combined.Contains("dismissed")
               || combined.Contains("cancelled")
               || combined.Contains("отмен");
    }

    private enum Section
    {
        None,
        Stdout,
        Stderr,
    }
}
