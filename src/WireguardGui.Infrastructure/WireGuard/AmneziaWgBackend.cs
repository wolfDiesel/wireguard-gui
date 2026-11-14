using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class AmneziaWgBackend(
    IProcessRunner processRunner,
    IProfileStore profileStore) : QuickWireGuardBackend(processRunner, profileStore)
{
    public override BackendKind Kind => BackendKind.AmneziaWg;

    protected override string QuickCommand => "awg-quick";

    protected override string ShowCommand => "awg";
}
