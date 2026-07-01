using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.WireGuard;

public sealed class WireGuardBackendFactory(
    NativeWireGuardBackend native,
    NmcliWireGuardBackend nmcli) : IWireGuardBackendFactory
{
    public IWireGuardBackend GetBackend(BackendKind kind) =>
        kind switch
        {
            BackendKind.Native => native,
            BackendKind.Nmcli => nmcli,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
