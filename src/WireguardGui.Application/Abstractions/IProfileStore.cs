using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

public interface IProfileStore
{
    Task<IReadOnlyList<VpnProfile>> ListProfilesAsync(CancellationToken cancellationToken = default);
    Task<VpnProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);
    Task SaveProfileAsync(VpnProfile profile, CancellationToken cancellationToken = default);
    Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);
    string GetProfileDirectory(string profileId);
    string GetConfigPath(string profileId);
    string GetConfigPath(VpnProfile profile);
    string DataRoot { get; }
}
