using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class CloudflareSplitRouteSource : ISplitRouteSource
{
    public string ProgressKey => "Progress_Routes_Cloudflare";

    public bool IsEnabled(SplitRoutingSettings settings) => settings.IncludeCloudflare;

    public Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(ProgressKey);
        return Task.FromResult<IReadOnlyList<string>>(SplitRoutingConstants.CloudflareRoutes.ToList());
    }
}
