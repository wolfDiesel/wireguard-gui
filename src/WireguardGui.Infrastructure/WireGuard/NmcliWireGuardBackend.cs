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
                "NM import and connect: {Connection} ← {Config}",
                profile.ConnectionName,
                configPath);
            script =
                $"nmcli connection import type wireguard file {path}; " +
                $"nmcli connection modify {name} connection.autoconnect no; " +
                $"nmcli connection up {name}";
        }
        else
        {
            logger.LogInformation("NM connect: {Connection}", profile.ConnectionName);
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
                "nmcli up returned code {Code}, but connection {Connection} is active",
                result.ExitCode,
                profile.ConnectionName);
            return;
        }

        throw new WireGuardOperationException(
            "Failed to connect",
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

        throw new WireGuardOperationException("Failed to disconnect", result.StandardError.Trim());
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
            "NM reimport connection {Connection} from {Config}{Connect}",
            profile.ConnectionName,
            configPath,
            connectAfter ? " with connect" : string.Empty);

        var result = await processRunner.RunPrivilegedShellAsync(script, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
            throw new WireGuardOperationException(
                "Failed to import config into NetworkManager",
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
            "Failed to connect after reimport",
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
        logger.LogInformation("Deleting NM connection {Connection}", profile.ConnectionName);

        var result = await processRunner.RunPrivilegedShellAsync(
            $"nmcli connection down {name} 2>/dev/null || true; nmcli connection delete {name}",
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess || NmcliConnectionHelper.IsUnknownConnection(result))
            return;

        throw new WireGuardOperationException(
            "Failed to delete connection from NetworkManager",
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
            "Removed Name= from config {Config} — NM name comes from filename",
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
            "NM connection name {Old} → {New} for profile {Profile}",
            profile.ConnectionName,
            importedName,
            profile.Name);

        var updated = profile with { ConnectionName = importedName };
        await profileStore.SaveProfileAsync(updated, cancellationToken).ConfigureAwait(false);
    }
}
