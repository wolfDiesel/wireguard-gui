namespace WireguardGui.Domain;

public sealed record VpnProfile(
    string Id,
    string Name,
    BackendKind Backend,
    string ConnectionName,
    DateTimeOffset ImportedAt,
    SplitRoutingSettings SplitRouting,
    string ConfigFileName = "wireguard.conf")
{
    public static VpnProfile Create(string name, BackendKind backend, string connectionName) =>
        new(
            Guid.NewGuid().ToString("N"),
            name,
            backend,
            connectionName,
            DateTimeOffset.UtcNow,
            SplitRoutingSettings.CreateDefault());
}
