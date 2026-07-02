using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface ISplitRouteSource
{
    string ProgressKey { get; }

    bool IsEnabled(SplitRoutingSettings settings);

    Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
