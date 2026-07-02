using WireguardGui.Domain;

namespace WireguardGui.Application.Contracts;

public sealed record ProfileSummaryDto(
    string Id,
    string Name,
    string ConnectionName,
    BackendKind Backend,
    ConnectionState State,
    bool SplitRoutingEnabled);

public sealed record ImportProfileRequestDto(
    string SourceConfigPath,
    BackendKind Backend);

public sealed record ImportProfileResultDto(
    bool Success,
    string? ProfileId,
    string? ErrorMessage);

public sealed record SystemCapabilitiesDto(
    IReadOnlyList<BackendCapabilityDto> Backends,
    bool AnyAvailable);

public sealed record BackendCapabilityDto(
    BackendKind Backend,
    bool IsAvailable,
    IReadOnlyList<string> MissingComponents,
    string FedoraInstallHint,
    string DebianInstallHint);

public sealed record SplitRoutingResultDto(
    bool Success,
    int RouteCount,
    string? RoutesCsv,
    string? ErrorMessage);

public sealed record SplitRoutingSettingsResultDto(
    bool Success,
    SplitRoutingSettings? Settings,
    string? ErrorMessage);

public enum OperationErrorCode
{
    None,
    ProfileNotFound,
    BackendUnavailable,
    ConfigNotFound,
    ConfigInvalid,
    ConnectionFailed,
    SplitRoutingDisabled,
    NoRoutesGenerated,
    SaveFailed,
    DeleteFailed,
    Unknown,
}

public sealed record OperationResultDto(
    bool Success,
    OperationErrorCode ErrorCode = OperationErrorCode.None,
    string? ErrorMessage = null,
    string? WarningMessage = null);
