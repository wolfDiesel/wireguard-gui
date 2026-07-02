using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class CloudflareSplitRouteSource : ISplitRouteSource
{
    public int Priority => 0;

    public bool IsEnabled(SplitRoutingSettings settings) => settings.IncludeCloudflare;

    public Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SplitRoutingProgress("Progress_Routes_Cloudflare"));
        return Task.FromResult<IReadOnlyList<string>>(SplitRoutingConstants.CloudflareRoutes);
    }
}
