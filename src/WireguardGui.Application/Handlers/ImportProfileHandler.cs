using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Contracts;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;

namespace WireguardGui.Application.Handlers;

public sealed class ImportProfileHandler(
    IProfileStore profileStore,
    IWireGuardConfigValidator validator,
    IWireGuardConfigParser configParser,
    ISystemCapabilityProbe capabilityProbe,
    ILogger<ImportProfileHandler> logger)
{
    public async Task<ImportProfileResultDto> HandleAsync(
        ImportProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var capabilities = await capabilityProbe.ProbeAllAsync(cancellationToken);
        var backendCap = capabilities.FirstOrDefault(c => c.Backend == request.Backend);
        if (backendCap is null || !backendCap.IsAvailable)
            return new ImportProfileResultDto(false, null, $"{request.Backend} backend is unavailable");

        if (!File.Exists(request.SourceConfigPath))
            return new ImportProfileResultDto(false, null, "Configuration file not found");

        string configContent;
        try
        {
            configContent = await File.ReadAllTextAsync(request.SourceConfigPath, cancellationToken);
            validator.Validate(configContent);
        }
        catch (WireGuardConfigValidationException ex)
        {
            return new ImportProfileResultDto(false, null, ex.Message);
        }

        var fileName = Path.GetFileNameWithoutExtension(request.SourceConfigPath);
        var interfaceName = configParser.ReadInterfaceName(configContent);
        var connectionName = request.Backend == BackendKind.Nmcli
            ? fileName
            : !string.IsNullOrWhiteSpace(interfaceName) ? interfaceName : fileName;
        var configFileName = $"{connectionName}.conf";
        if (request.Backend == BackendKind.Nmcli)
            configContent = configParser.RemoveInterfaceName(configContent);

        var profile = VpnProfile.Create(fileName, request.Backend, connectionName) with
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
