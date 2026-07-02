using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting.Sources;

internal sealed class TelegramSplitRouteSource : ISplitRouteSource
{
    public int Priority => 0;

    public bool IsEnabled(SplitRoutingSettings settings) => settings.Telegram;

    public Task<IReadOnlyList<string>> CollectAsync(
        SplitRoutingSettings settings,
        IProgress<SplitRoutingProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SplitRoutingProgress("Progress_Routes_Telegram"));
        return Task.FromResult<IReadOnlyList<string>>(SplitRoutingConstants.TelegramRoutes);
    }
}
