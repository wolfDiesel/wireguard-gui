namespace WireguardGui.Application.Exceptions;

public class WireGuardOperationException(string message, string? details = null)
    : Exception(details is null ? message : $"{message}: {details}")
{
    public string UserMessage { get; } = message;
    public string? Details { get; } = details;
}

public sealed class WireGuardConfigValidationException(string message) : Exception(message);

public sealed class PrivilegeRequiredException(string message = "Нужны права администратора")
    : WireGuardOperationException(message);
