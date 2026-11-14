using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.Storage;

public sealed class ProfileImporter(
    IProfileStore profileStore,
    IWireGuardConfigValidator validator,
    IWireGuardConfigParser configParser,
    ILogger<ProfileImporter> logger) : IProfileImporter
{
    private const string AmneziaWgBackendRequiredMessage =
        "This is an AmneziaWG config; choose the AmneziaWG backend to import it";

    public async Task<ImportProfileResultDto> ImportFromFileAsync(
        string sourceConfigPath,
        BackendKind backend,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceConfigPath))
            return new ImportProfileResultDto(false, null, "Configuration file not found");

        string configContent;
        try
        {
            configContent = await File.ReadAllTextAsync(sourceConfigPath, cancellationToken);
            validator.Validate(configContent);
        }
        catch (WireGuardConfigValidationException ex)
        {
            return new ImportProfileResultDto(false, null, ex.Message);
        }

        if (configParser.IsAmneziaWg(configContent) && backend != BackendKind.AmneziaWg)
            return new ImportProfileResultDto(false, null, AmneziaWgBackendRequiredMessage);

        var fileName = Path.GetFileNameWithoutExtension(sourceConfigPath);
        var interfaceName = configParser.ReadInterfaceName(configContent);

        var connectionName = backend switch
        {
            BackendKind.Nmcli => fileName,
            BackendKind.AmneziaWg => await ResolveAmneziaWgConnectionNameAsync(
                string.IsNullOrWhiteSpace(interfaceName) ? fileName : interfaceName,
                cancellationToken),
            _ => !string.IsNullOrWhiteSpace(interfaceName) ? interfaceName : fileName,
        };

        if (!IsValidConnectionName(backend, connectionName))
            return new ImportProfileResultDto(false, null, "Invalid connection name in configuration");

        var configFileName = $"{connectionName}.conf";
        if (backend is BackendKind.Nmcli or BackendKind.AmneziaWg)
            configContent = configParser.RemoveInterfaceName(configContent);

        var importedDns = configParser.ReadDnsServers(configContent);
        var tunnelDns = importedDns.Count > 0
            ? string.Join(", ", importedDns)
            : TunnelDnsServers.Default;
        var splitRouting = SplitRoutingSettings.CreateDefault() with { TunnelDns = tunnelDns };

        var profile = VpnProfile.Create(fileName, backend, connectionName) with
        {
            ConfigFileName = configFileName,
            SplitRouting = splitRouting,
        };

        var profileDir = profileStore.GetProfileDirectory(profile.Id);
        Directory.CreateDirectory(profileDir);
        var destPath = profileStore.GetConfigPath(profile);
        await File.WriteAllTextAsync(destPath, configContent, cancellationToken);

        await profileStore.SaveProfileAsync(profile, cancellationToken);
        logger.LogInformation(
            "Imported profile {Name} ({Backend}), connection {Connection}",
            profile.Name,
            profile.Backend,
            profile.ConnectionName);

        return new ImportProfileResultDto(true, profile.Id, null);
    }

    private static bool IsValidConnectionName(BackendKind backend, string connectionName) =>
        backend == BackendKind.AmneziaWg
            ? VpnProfileNaming.IsValidAmneziaWgInterfaceName(connectionName)
            : VpnProfileNaming.IsValidConnectionName(connectionName);

    private async Task<string> ResolveAmneziaWgConnectionNameAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        var profiles = await profileStore.ListProfilesAsync(cancellationToken);
        var taken = profiles.Select(static profile => profile.ConnectionName).ToList();
        return VpnProfileNaming.MakeUniqueAmneziaWgInterfaceName(candidate, taken);
    }
}
