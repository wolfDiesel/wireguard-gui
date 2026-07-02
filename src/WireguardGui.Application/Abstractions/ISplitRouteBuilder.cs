using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface ISplitRouteBuilder
{
    Task<IReadOnlyList<string>> BuildRoutesAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
