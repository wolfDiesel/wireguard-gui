using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

internal sealed record TunnelToolset(string ShowCommand)
{
    public static TunnelToolset For(BackendKind backend) =>
        new(backend == BackendKind.AmneziaWg ? "awg" : "wg");
}
