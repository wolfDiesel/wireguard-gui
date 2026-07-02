using System.Text.RegularExpressions;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Infrastructure.SplitRouting;

internal sealed partial class DomainDnsResolver(IProcessRunner processRunner)
{
    private static readonly SemaphoreSlim ResolveGate = new(6, 6);

    public async Task<IReadOnlyList<string>> ResolveIpv4Async(
        string domain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return [];

        EnsureDigAvailable();

        await ResolveGate.WaitAsync(cancellationToken);
        try
        {
            var result = await processRunner.RunAsync(
                "dig",
                ["+short", "A", domain.Trim()],
                cancellationToken);

            if (!result.IsSuccess)
                return [];

            return result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => IpV4Pattern().IsMatch(line))
                .Distinct()
                .ToList();
        }
        finally
        {
            ResolveGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ResolveIpv4ParallelAsync(
        IEnumerable<string> domains,
        CancellationToken cancellationToken)
    {
        var tasks = domains
            .Select(d => ResolveIpv4Async(d, cancellationToken))
            .ToList();
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).Distinct().ToList();
    }

    public void EnsureDigAvailable()
    {
        if (!processRunner.IsCommandAvailable("dig"))
            throw new WireGuardOperationException("Domain resolution requires dig (bind-utils / dnsutils)");
    }

    [GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}$")]
    private static partial Regex IpV4Pattern();
}
