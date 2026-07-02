using Microsoft.Extensions.Logging.Abstractions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Infrastructure.Tests.Fakes;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public Dictionary<string, string> DigResponses { get; init; } = new();

    public bool IsCommandAvailable(string command) => command is "dig" or "wg" or "wg-quick" or "nmcli";

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (fileName == "dig" && arguments.Count >= 3)
        {
            var domain = arguments[^1];
            var output = DigResponses.GetValueOrDefault(domain, string.Empty);
            return Task.FromResult(new ProcessResult(0, output, string.Empty));
        }

        if (fileName == "wg" && arguments.Count >= 1 && arguments[0] == "show")
            return Task.FromResult(new ProcessResult(0, WgShowOutput, string.Empty));

        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    public Task<ProcessResult> RunPrivilegedAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
        RunAsync(fileName, arguments, cancellationToken);

    public Task<ProcessResult> RunPrivilegedShellAsync(string script, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

    public string WgShowOutput { get; set; } = string.Empty;
}

internal static class TestStoreFactory
{
    public static JsonProfileStore Create(string root) =>
        new(root, NullLogger<JsonProfileStore>.Instance);
}
