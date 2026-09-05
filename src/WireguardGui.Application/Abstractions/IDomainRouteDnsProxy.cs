namespace WireguardGui.Application.Abstractions;

/// <summary>
/// Запасной механизм перехвата DNS-ответов для динамических split-routing маршрутов.
/// Работает как UDP-прокси на 127.0.0.1:<see cref="ListenPort"/>: перехватывает DNS-запросы,
/// форвардит их на апстрим-резолвер, парсит ответ и при совпадении суффиксов
/// вызывает <c>onResolvedHosts</c> с найденными IP-адресами. Не требует привилегий.
/// </summary>
/// <remarks>
/// ВНИМАНИЕ: в текущей версии НЕ подключён к рабочему процессу — <see cref="StartAsync"/>
/// нигде не вызывается. Основной путь обновления DNS-маршрутов —
/// <c>ResolvedDnsRouteMonitor</c> через <c>resolvectl monitor</c> (FIFO + pkexec).
/// Прокси следует включать, когда systemd-resolved недоступен, монитор падает
/// или нужен путь без pkexec.
/// </remarks>
public interface IDomainRouteDnsProxy
{
    /// <summary>Порт UDP-прокси на loopback (127.0.0.1).</summary>
    const int ListenPort = 5399;

    bool IsRunning { get; }

    Task StartAsync(
        string profileId,
        IReadOnlyList<string> suffixes,
        IReadOnlyList<string> upstreamDns,
        Func<IReadOnlyList<string>, CancellationToken, Task> onResolvedHosts,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
