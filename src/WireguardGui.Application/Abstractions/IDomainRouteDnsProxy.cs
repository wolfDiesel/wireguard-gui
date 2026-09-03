namespace WireguardGui.Application.Abstractions;

public interface IDomainRouteDnsProxy
{
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
