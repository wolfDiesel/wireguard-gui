namespace WireguardGui.Infrastructure.SplitRouting;

/// <summary>
/// Состояние туннеля по данным WireGuard. Используется, чтобы не уводить DNS
/// split-доменов в туннель, который не может их обслужить (нет handshake —
/// типичная ситуация после выхода из спящего режима или падения VPN-сервера).
/// </summary>
public enum TunnelHealthState
{
    /// <summary>Проверить не удалось (нет wg, нет прав, нет пиров) — ведём себя как раньше.</summary>
    Unknown,

    /// <summary>Недавний handshake есть.</summary>
    Healthy,

    /// <summary>Handshake был, но давно: туннель не отвечает.</summary>
    Stale,

    /// <summary>Handshake не проходил ни разу.</summary>
    NoHandshake,

    /// <summary>Интерфейса туннеля нет в системе.</summary>
    InterfaceMissing,
}

public sealed record TunnelHealth(TunnelHealthState State, string? Detail)
{
    public static readonly TunnelHealth Unknown = new(TunnelHealthState.Unknown, null);

    /// <summary>
    /// Можно ли отправлять DNS split-доменов в туннель. При <see cref="TunnelHealthState.Unknown"/>
    /// сохраняется прежнее поведение: без данных о handshake считаем туннель рабочим.
    /// </summary>
    public bool AllowsSplitDns => State is TunnelHealthState.Healthy or TunnelHealthState.Unknown;

    public string Describe() => Detail is null ? State.ToString() : $"{State}: {Detail}";
}
