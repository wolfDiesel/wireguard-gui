using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Infrastructure.Tests.Fakes;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public Dictionary<string, string> DigResponses { get; init; } = new();

    public Dictionary<string, string> TunnelDigResponses { get; init; } = new();

    public List<DigCall> DigCalls { get; } = [];

    public List<IReadOnlyList<string>> RawDigArguments { get; } = [];

    public int DigFailureExitCode { get; init; }

    public bool IsCommandAvailable(string command) => command is "dig" or "wg" or "wg-quick" or "nmcli";

    public List<string> CommandLines { get; } = [];

    public Dictionary<string, ProcessResult> CommandResults { get; } = new(StringComparer.Ordinal);

    public string AwgShowOutput { get; set; } = string.Empty;

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var commandLine = fileName + " " + string.Join(' ', arguments);
        CommandLines.Add(commandLine);
        if (CommandResults.TryGetValue(commandLine, out var overrideResult))
            return Task.FromResult(overrideResult);

        if (fileName == "dig")
        {
            var call = ParseDigArguments(arguments);
            DigCalls.Add(call);
            RawDigArguments.Add(arguments.ToArray());
            var output = call.Server is null
                ? DigResponses.GetValueOrDefault(call.Domain, string.Empty)
                : TunnelDigResponses.GetValueOrDefault($"{call.Server}|{call.RecordType}|{call.Domain}", string.Empty);

            return Task.FromResult(new ProcessResult(DigFailureExitCode, output, string.Empty));
        }

        if (fileName == "wg" && arguments.Count >= 1 && arguments[0] == "show")
            return Task.FromResult(new ProcessResult(0, WgShowOutput, string.Empty));

        if (fileName == "awg" && arguments.Count >= 1 && arguments[0] == "show")
            return Task.FromResult(new ProcessResult(0, AwgShowOutput, string.Empty));

        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    public Task<ProcessResult> RunPrivilegedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        PrivilegedCallCount++;
        return RunAsync(fileName, arguments, cancellationToken);
    }

    public Task<ProcessResult> RunPrivilegedShellAsync(string script, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

    public bool HasActivePrivilegedSession { get; set; }

    public string WgShowOutput { get; set; } = string.Empty;

    public int PrivilegedCallCount { get; private set; }

    internal static DigCall ParseDigArguments(IReadOnlyList<string> arguments)
    {
        string? server = null;
        string recordType = string.Empty;
        string domain = string.Empty;

        foreach (var argument in arguments)
        {
            if (argument.StartsWith('@'))
            {
                server = argument[1..];
                continue;
            }

            if (argument.StartsWith('+'))
                continue;

            if (argument is "A" or "AAAA")
            {
                recordType = argument;
                continue;
            }

            domain = argument;
        }

        return new DigCall(domain, recordType, server);
    }
}

internal readonly record struct DigCall(string Domain, string RecordType, string? Server);

internal static class TestStoreFactory
{
    public static JsonProfileStore Create(string root) =>
        new(root, NullLogger<JsonProfileStore>.Instance);
}
