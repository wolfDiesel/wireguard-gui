using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public abstract class QuickWireGuardBackend(
    IProcessRunner processRunner,
    IProfileStore profileStore) : IWireGuardBackend
{
    protected abstract string QuickCommand { get; }

    protected abstract string ShowCommand { get; }

    public abstract BackendKind Kind { get; }

    public async Task ConnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var result = await processRunner.RunPrivilegedAsync(
            QuickCommand,
            ["up", configPath],
            cancellationToken);

        if (!result.IsSuccess)
            throw new WireGuardOperationException("Failed to bring up tunnel", result.StandardError.Trim());
    }

    public async Task DisconnectAsync(VpnProfile profile, CancellationToken cancellationToken = default)
    {
        var configPath = profileStore.GetConfigPath(profile);
        var result = await processRunner.RunPrivilegedAsync(
            QuickCommand,
            ["down", configPath],
            cancellationToken);

        if (!result.IsSuccess && !IsMissingInterfaceError(result.StandardError))
            throw new WireGuardOperationException("Failed to bring down tunnel", result.StandardError.Trim());
    }

    public async Task<ConnectionState> GetConnectionStateAsync(
        VpnProfile profile,
        CancellationToken cancellationToken = default)
    {
        // show требует CAP_NET_ADMIN; если pkexec-сессия уже авторизована
        // (пользователь подключал/отключал интерфейс), читаем статус через неё —
        // иначе деградируем до прежнего поведения (без прав, статус Unknown).
        var result = processRunner.HasActivePrivilegedSession
            ? await processRunner.RunPrivilegedAsync(ShowCommand, ["show"], cancellationToken)
            : await processRunner.RunAsync(ShowCommand, ["show"], cancellationToken);
        if (!result.IsSuccess)
            return ConnectionState.Unknown;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("interface:", StringComparison.OrdinalIgnoreCase))
                continue;

            var iface = line["interface:".Length..].Trim();
            if (string.Equals(iface, profile.ConnectionName, StringComparison.Ordinal))
                return ConnectionState.Connected;
        }

        return ConnectionState.Disconnected;
    }

    public async Task ReimportFromConfigAsync(
        VpnProfile profile,
        bool connectAfter,
        CancellationToken cancellationToken = default)
    {
        var wasConnected = await GetConnectionStateAsync(profile, cancellationToken) == ConnectionState.Connected;
        if (!wasConnected && !connectAfter)
            return;

        if (wasConnected)
            await DisconnectAsync(profile, cancellationToken);

        if (connectAfter || wasConnected)
            await ConnectAsync(profile, cancellationToken);
    }

    public Task UnregisterAsync(VpnProfile profile, CancellationToken cancellationToken = default) =>
        DisconnectAsync(profile, cancellationToken);

    private static bool IsMissingInterfaceError(string standardError) =>
        standardError.Contains("is not a WireGuard interface", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("is not an AmneziaWG interface", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("Cannot find device", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
}
