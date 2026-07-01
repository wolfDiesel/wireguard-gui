using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface ISplitRouteBuilder
{
    Task<IReadOnlyList<string>> BuildRoutesAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
