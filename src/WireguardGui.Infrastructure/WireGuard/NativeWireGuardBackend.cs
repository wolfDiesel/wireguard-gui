using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class NativeWireGuardBackend(
    IProcessRunner processRunner,
    IProfileStore profileStore) : QuickWireGuardBackend(processRunner, profileStore)
{
    public override BackendKind Kind => BackendKind.Native;

    protected override string QuickCommand => "wg-quick";

    protected override string ShowCommand => "wg";
}
