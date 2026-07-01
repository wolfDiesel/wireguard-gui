using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class NmcliWireGuardBackend(
    IProcessRunner processRunner,
    IProfileStore profileStore,
    IWireGuardConfigParser configParser,
    ILogger<NmcliWireGuardBackend> logger) : IWireGuardBackend
{
    public BackendKind Kind => BackendKind.Nmcli;

    public Task ImportAsync(VpnProfile profile, string configPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var exists = await NmcliConnectionHelper.ExistsAsync(
            processRunner,
            profile.ConnectionName,
            cancellationToken).ConfigureAwait(false);

        var configPath = await PrepareConfigPathAsync(profile, cancellationToken).ConfigureAwait(false);
        var name = NmcliConnectionHelper.ShellQuote(profile.ConnectionName);
        var path = NmcliConnectionHelper.ShellQuote(configPath);

        string script;
        if (!exists)
        {
            logger.LogInformation(
                "Импорт и подключение NM: {Connection} ← {Config}",
                profile.ConnectionName,
                configPath);
            script =
                $"nmcli connection import type wireguard file {path}; " +
                $"nmcli connection modify {name} connection.autoconnect no; " +
                $"nmcli connection up {name}";
        }
        else
        {
            logger.LogInformation("Подключение NM: {Connection}", profile.ConnectionName);
            script = $"nmcli connection up {name}";
        }

        var result = await processRunner.RunPrivilegedShellAsync(script, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
            await SyncConnectionNameFromImportAsync(profile, result, cancellationToken).ConfigureAwait(false);

        profile = await profileStore.GetProfileAsync(profile.Id, cancellationToken).ConfigureAwait(false) ?? profile;

        if (result.IsSuccess)
            return;

        var state = await GetConnectionStateAsync(profile, cancellationToken).ConfigureAwait(false);
        if (state == ConnectionState.Connected)
        {
            logger.LogWarning(
                "nmcli up вернул код {Code}, но соединение {Connection} активно",
                result.ExitCode,
                profile.ConnectionName);
            return;
        }

        throw new WireGuardOperationException(
            "Не удалось подключить соединение",
            string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput.Trim() : result.StandardError.Trim());
    }

    public async Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        if (!await NmcliConnectionHelper.ExistsAsync(processRunner, profile.ConnectionName, cancellationToken)
                .ConfigureAwait(false))
            return;

        var state = await GetConnectionStateAsync(profile, cancellationToken).ConfigureAwait(false);
        if (state != ConnectionState.Connected)
            return;

        var result = await processRunner.RunPrivilegedAsync(
            "nmcli",
            ["connection", "down", profile.ConnectionName],
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess || NmcliConnectionHelper.IsInactiveError(result))
            return;

        throw new WireGuardOperationException("Не удалось отключить соединение", result.StandardError.Trim());
    }

    public async Task<ConnectionState> GetConnectionStateAsync(
        VpnProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!await NmcliConnectionHelper.ExistsAsync(processRunner, profile.ConnectionName, cancellationToken)
                .ConfigureAwait(false))
            return ConnectionState.Disconnected;

        var result = await processRunner.RunAsync(
            "nmcli",
            ["-t", "-f", "GENERAL.STATE", "connection", "show", profile.ConnectionName],
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            return ConnectionState.Unknown;

        return result.StandardOutput.Contains("activated", StringComparison.OrdinalIgnoreCase)
            ? ConnectionState.Connected
            : ConnectionState.Disconnected;
    }

    public Task ApplyRoutesAsync(
        VpnProfile profile,
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken = default) =>
        ReimportFromConfigAsync(profile, connectAfter: true, cancellationToken);

    public async Task ReimportFromConfigAsync(
        VpnProfile profile,
        bool connectAfter,
        CancellationToken cancellationToken = default)
    {
        var configPath = await PrepareConfigPathAsync(profile, cancellationToken).ConfigureAwait(false);
        var name = NmcliConnectionHelper.ShellQuote(profile.ConnectionName);
        var path = NmcliConnectionHelper.ShellQuote(configPath);

        var script =
            $"nmcli connection down {name} 2>/dev/null || true; " +
            $"nmcli connection delete {name} 2>/dev/null || true; " +
            $"nmcli connection import type wireguard file {path}; " +
            $"nmcli connection modify {name} connection.autoconnect no";
        if (connectAfter)
            script += $"; nmcli connection up {name}";

        logger.LogInformation(
            "Переимпорт NM-соединения {Connection} из {Config}{Connect}",
            profile.ConnectionName,
            configPath,
            connectAfter ? " с подключением" : string.Empty);

        var result = await processRunner.RunPrivilegedShellAsync(script, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
            throw new WireGuardOperationException(
                "Не удалось импортировать конфиг в NetworkManager",
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim());

        await SyncConnectionNameFromImportAsync(profile, result, cancellationToken).ConfigureAwait(false);
        profile = await profileStore.GetProfileAsync(profile.Id, cancellationToken).ConfigureAwait(false) ?? profile;

        if (!connectAfter)
            return;

        var state = await GetConnectionStateAsync(profile, cancellationToken).ConfigureAwait(false);
        if (state == ConnectionState.Connected)
            return;

        throw new WireGuardOperationException(
            "Не удалось подключить соединение после переимпорта",
            string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim());
    }

    public async Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        if (!await NmcliConnectionHelper.ExistsAsync(processRunner, profile.ConnectionName, cancellationToken)
                .ConfigureAwait(false))
            return;

        var name = NmcliConnectionHelper.ShellQuote(profile.ConnectionName);
        logger.LogInformation("Удаление NM-соединения {Connection}", profile.ConnectionName);

        var result = await processRunner.RunPrivilegedShellAsync(
            $"nmcli connection down {name} 2>/dev/null || true; nmcli connection delete {name}",
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess || NmcliConnectionHelper.IsUnknownConnection(result))
            return;

        throw new WireGuardOperationException(
            "Не удалось удалить соединение из NetworkManager",
            result.StandardError.Trim());
    }

    private async Task<string> PrepareConfigPathAsync(
        VpnProfile profile,
        CancellationToken cancellationToken)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var content = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var cleaned = configParser.RemoveInterfaceName(content);
        if (string.Equals(content, cleaned, StringComparison.Ordinal))
            return configPath;

        await File.WriteAllTextAsync(configPath, cleaned, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Удалён Name= из конфига {Config} — имя NM берётся из файла",
            profile.ConfigFileName);
        return configPath;
    }

    private async Task SyncConnectionNameFromImportAsync(
        VpnProfile profile,
        ProcessResult importResult,
        CancellationToken cancellationToken)
    {
        var importedName = NmcliConnectionHelper.ParseImportedConnectionName(
            $"{importResult.StandardOutput}\n{importResult.StandardError}");
        if (string.IsNullOrWhiteSpace(importedName) ||
            string.Equals(importedName, profile.ConnectionName, StringComparison.Ordinal))
            return;

        logger.LogInformation(
            "NM-имя соединения {Old} → {New} для профиля {Profile}",
            profile.ConnectionName,
            importedName,
            profile.Name);

        var updated = profile with { ConnectionName = importedName };
        await profileStore.SaveProfileAsync(updated, cancellationToken).ConfigureAwait(false);
    }
}
