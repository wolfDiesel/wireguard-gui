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

public sealed record SplitRoutingToolingDto(
    bool HasIp,
    bool HasDig,
    bool HasResolvectl,
    IReadOnlyList<string> MissingCommands,
    string FedoraInstallHint,
    string DebianInstallHint,
    bool HasMissingCommands,
    bool PolicyRoutingAvailable,
    bool DnsMonitorAvailable,
    bool DomainResolveAvailable);

public sealed record SplitRoutingResultDto(
    bool Success,
    int RouteCount,
    string? RoutesCsv,
    string? ErrorMessage);

public sealed record SplitRoutingSettingsResultDto(
    bool Success,
    SplitRoutingSettings? Settings,
    string? TunnelDnsPlaceholder,
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

public sealed record AmneziaWgStatusDto(
    bool IsAvailable,
    IReadOnlyList<string> MissingComponents,
    string? ToolsVersion,
    AmneziaWgTransport Transport,
    string? TransportVersion,
    DistroFamily Distro);

public sealed record BackendVersionDto(
    string? ToolsVersion,
    AmneziaWgTransport Transport,
    string? TransportVersion);

public sealed record BackendInstallStepDto(
    string Title,
    string Script,
    bool BestEffort = false);

public sealed record BackendInstallPlanDto(
    DistroFamily Family,
    IReadOnlyList<BackendInstallStepDto> Steps);

public sealed record BackendInstallProgressDto(
    int StepIndex,
    int StepCount,
    string Title,
    string? Output);

public sealed record BackendInstallResultDto(
    bool Success,
    string? ErrorMessage = null,
    string? LastOutput = null,
    bool NeedsAdmin = false);
