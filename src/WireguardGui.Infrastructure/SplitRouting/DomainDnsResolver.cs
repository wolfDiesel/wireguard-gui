using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;

namespace WireguardGui.Infrastructure.SplitRouting;

internal sealed partial class DomainDnsResolver(
    IProcessRunner processRunner,
    ILogger<DomainDnsResolver>? logger = null)
{
    private const string TimeoutOption = "+time=2";
    private const string AttemptsOption = "+tries=1";

    private static readonly SemaphoreSlim ResolveGate = new(6, 6);

    public Task<IReadOnlyList<string>> ResolveIpv6Async(
        string domain,
        CancellationToken cancellationToken) =>
        ResolveIpv6Async(domain, [], cancellationToken);

    public Task<IReadOnlyList<string>> ResolveIpv4Async(
        string domain,
        CancellationToken cancellationToken) =>
        ResolveIpv4Async(domain, [], cancellationToken);

    public async Task<IReadOnlyList<string>> ResolveIpv6Async(
        string domain,
        IReadOnlyList<string> upstream,
        CancellationToken cancellationToken)
    {
        var answers = await QueryAsync(domain, "AAAA", upstream, cancellationToken).ConfigureAwait(false);

        return answers
            .Where(line => line.Contains(':', StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ResolveIpv4Async(
        string domain,
        IReadOnlyList<string> upstream,
        CancellationToken cancellationToken)
    {
        var answers = await QueryAsync(domain, "A", upstream, cancellationToken).ConfigureAwait(false);

        return answers
            .Where(line => IpV4Pattern().IsMatch(line))
            .Distinct()
            .ToList();
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

    private async Task<IReadOnlyList<string>> QueryAsync(
        string domain,
        string recordType,
        IReadOnlyList<string> upstream,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return [];

        EnsureDigAvailable();

        await ResolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var name = domain.Trim();
            foreach (var server in upstream)
            {
                var tunnelAnswers = await RunDigAsync(name, recordType, server, cancellationToken)
                    .ConfigureAwait(false);
                if (tunnelAnswers.Count > 0)
                {
                    logger?.LogDebug("DNS {Domain} {Type}: answered by tunnel DNS {Server}", name, recordType, server);
                    return tunnelAnswers;
                }
            }

            if (upstream.Count > 0)
            {
                logger?.LogDebug(
                    "DNS {Domain} {Type}: no answer from tunnel DNS {Servers}, falling back to the system resolver",
                    name,
                    recordType,
                    string.Join(", ", upstream));
            }

            return await RunDigAsync(name, recordType, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ResolveGate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> RunDigAsync(
        string domain,
        string recordType,
        string? server,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "+short" };
        if (server is not null)
        {
            arguments.Add(TimeoutOption);
            arguments.Add(AttemptsOption);
        }

        arguments.Add(recordType);
        arguments.Add(domain);
        if (server is not null)
            arguments.Add("@" + server);

        var result = await processRunner.RunAsync("dig", arguments, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            return [];

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}$")]
    private static partial Regex IpV4Pattern();
}
