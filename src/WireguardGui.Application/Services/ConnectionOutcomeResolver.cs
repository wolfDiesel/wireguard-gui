using WireguardGui.Application.Contracts;
using WireguardGui.Domain;

namespace WireguardGui.Application.Services;

internal static class ConnectionOutcomeResolver
{
    public static OperationResultDto ResolveAfterFailure(
        ConnectionState state,
        string failureMessage,
        OperationErrorCode errorCode = OperationErrorCode.ConnectionFailed)
    {
        if (state == ConnectionState.Connected)
            return new OperationResultDto(true, OperationErrorCode.None, null, failureMessage);

        return new OperationResultDto(false, errorCode, failureMessage);
    }

    public static SplitRoutingResultDto ResolveSplitRoutingAfterFailure(
        ConnectionState state,
        int routeCount,
        string? routesCsv,
        string failureMessage)
    {
        if (state == ConnectionState.Connected)
            return new SplitRoutingResultDto(true, routeCount, routesCsv, null);

        return new SplitRoutingResultDto(false, routeCount, routesCsv, failureMessage);
    }
}
