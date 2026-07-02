using WireguardGui.Application.Contracts;

namespace WireguardGui.App.Avalonia.Services;

internal static class OperationErrorMapper
{
    public static string? Map(Localization.LocalizationService localization, OperationErrorCode code) =>
        code switch
        {
            OperationErrorCode.None => null,
            OperationErrorCode.ProfileNotFound => localization.Get("Error_ProfileNotFound"),
            OperationErrorCode.BackendUnavailable => localization.Get("Error_BackendUnavailable"),
            OperationErrorCode.ConfigNotFound => localization.Get("Error_ConfigNotFound"),
            OperationErrorCode.ConfigInvalid => localization.Get("Error_ConfigInvalid"),
            OperationErrorCode.ConnectionFailed => localization.Get("Error_ConnectionFailed"),
            OperationErrorCode.SplitRoutingDisabled => localization.Get("Error_SplitRoutingDisabled"),
            OperationErrorCode.NoRoutesGenerated => localization.Get("Error_NoRoutesGenerated"),
            OperationErrorCode.SaveFailed => localization.Get("Error_SaveFailed"),
            OperationErrorCode.DeleteFailed => localization.Get("Error_DeleteFailed"),
            _ => localization.Get("Error_Unknown"),
        };

    public static string ResolveMessage(
        Localization.LocalizationService localization,
        OperationErrorCode code,
        string? fallback) =>
        Map(localization, code) ?? fallback ?? localization.Get("Error_Unknown");
}
