using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IResolvedDnsRouteMonitor
{
    bool IsRunning { get; }

    Task StartAsync(VpnProfile profile, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
