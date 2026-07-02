using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface ISplitRouteSource
{
    int Priority { get; }

    bool IsEnabled(SplitRoutingSettings settings);

    Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken);
}
