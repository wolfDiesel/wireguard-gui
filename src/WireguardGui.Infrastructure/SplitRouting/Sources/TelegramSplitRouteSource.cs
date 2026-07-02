using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class TelegramSplitRouteSource : ISplitRouteSource
{
    public string ProgressKey => "Progress_Routes_Telegram";

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Telegram;

    public Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(ProgressKey);
        return Task.FromResult<IReadOnlyList<string>>(SplitRoutingConstants.TelegramRoutes.ToList());
    }
}
