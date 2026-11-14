using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

/// <summary>
/// Проба живости туннеля: наличие интерфейса и время последнего handshake.
/// Читает <c>wg show &lt;iface&gt; latest-handshakes</c> (или <c>awg show</c> для AmneziaWG),
/// при активной привилегированной сессии — через неё, иначе обычным запуском
/// (может не хватить прав).
/// </summary>
public sealed class TunnelHealthProbe(
    IProcessRunner processRunner,
    ILogger<TunnelHealthProbe>? logger = null)
{
    /// <summary>
    /// Keepalive профиля по умолчанию 25 с, значит handshake обязан обновляться
    /// заметно чаще; 5 минут — заведомо «мёртвый» туннель, но без ложных
    /// срабатываний при кратковременных провалах сети.
    /// </summary>
    public static readonly TimeSpan MaxHandshakeAge = TimeSpan.FromMinutes(5);

    public async Task<TunnelHealth> CheckAsync(
        VpnProfile profile,
        string iface,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iface))
            return new TunnelHealth(TunnelHealthState.InterfaceMissing, "interface name is unknown");

        var link = await processRunner
            .RunAsync("ip", ["link", "show", "dev", iface], cancellationToken)
            .ConfigureAwait(false);
        if (!link.IsSuccess)
            return new TunnelHealth(TunnelHealthState.InterfaceMissing, $"{iface} is not present");

        var toolset = TunnelToolset.For(profile.Backend);
        if (!processRunner.IsCommandAvailable(toolset.ShowCommand))
            return TunnelHealth.Unknown;

        var show = processRunner.HasActivePrivilegedSession
            ? await processRunner
                .RunPrivilegedAsync(toolset.ShowCommand, ["show", iface, "latest-handshakes"], cancellationToken)
                .ConfigureAwait(false)
            : await processRunner
                .RunAsync(toolset.ShowCommand, ["show", iface, "latest-handshakes"], cancellationToken)
                .ConfigureAwait(false);

        if (!show.IsSuccess)
        {
            logger?.LogDebug(
                "Tunnel health probe for {Interface}: {Command} show failed ({Error})",
                iface,
                toolset.ShowCommand,
                show.StandardError.Trim());
            return TunnelHealth.Unknown;
        }

        var health = Evaluate(show.StandardOutput, DateTimeOffset.UtcNow);
        if (!health.AllowsSplitDns)
            logger?.LogDebug("Tunnel health for {Interface}: {Health}", iface, health.Describe());

        return health;
    }

    internal static TunnelHealth Evaluate(string latestHandshakesOutput, DateTimeOffset now)
    {
        long latest = 0;
        var peers = 0;

        foreach (var line in latestHandshakesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var timestamp))
                continue;

            peers++;
            if (timestamp > latest)
                latest = timestamp;
        }

        if (peers == 0)
            return TunnelHealth.Unknown;

        if (latest <= 0)
            return new TunnelHealth(TunnelHealthState.NoHandshake, "peer has never completed a handshake");

        var age = now.ToUnixTimeSeconds() - latest;
        if (age > (long)MaxHandshakeAge.TotalSeconds)
            return new TunnelHealth(TunnelHealthState.Stale, $"last handshake {age} s ago");

        return new TunnelHealth(TunnelHealthState.Healthy, $"last handshake {Math.Max(age, 0)} s ago");
    }
}
