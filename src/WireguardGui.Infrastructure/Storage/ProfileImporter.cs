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

        var fileName = Path.GetFileNameWithoutExtension(sourceConfigPath);
        var interfaceName = configParser.ReadInterfaceName(configContent);
        var connectionName = backend == BackendKind.Nmcli
            ? fileName
            : !string.IsNullOrWhiteSpace(interfaceName) ? interfaceName : fileName;

        if (!VpnProfileNaming.IsValidConnectionName(connectionName))
            return new ImportProfileResultDto(false, null, "Invalid connection name in configuration");

        var configFileName = $"{connectionName}.conf";
        if (backend == BackendKind.Nmcli)
            configContent = configParser.RemoveInterfaceName(configContent);

        var profile = VpnProfile.Create(fileName, backend, connectionName) with
        {
            ConfigFileName = configFileName,
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
}
