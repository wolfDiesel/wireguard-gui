using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Exceptions;
using WireguardGui.Domain;
using WireguardGui.Infrastructure.Storage;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed partial class SplitRouteBuilder(
    IProcessRunner processRunner,
    HttpClient httpClient,
    ILogger<SplitRouteBuilder> logger) : ISplitRouteBuilder
{
    private static readonly string GoogleCachePath = Path.Combine(
        JsonProfileStore.GetDefaultDataRoot(),
        "google-ipranges-cache.json");

    public async Task<IReadOnlyList<string>> BuildRoutesAsync(
        SplitRoutingSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Сканирование маршрутов: YouTube={Youtube}, Telegram={Telegram}, Cloudflare={Cloudflare}, доменов={Domains}",
            settings.Youtube,
            settings.Telegram,
            settings.IncludeCloudflare,
            settings.CustomDomains.Count);

        var routes = new HashSet<string>(StringComparer.Ordinal);

        if (settings.Telegram)
        {
            progress?.Report("Progress_Routes_Telegram");
            foreach (var route in SplitRoutingConstants.TelegramRoutes)
                routes.Add(route);
            logger.LogInformation("Telegram: {Count} маршрутов", SplitRoutingConstants.TelegramRoutes.Count);
        }

        if (settings.IncludeCloudflare)
        {
            progress?.Report("Progress_Routes_Cloudflare");
            foreach (var route in SplitRoutingConstants.CloudflareRoutes)
                routes.Add(route);
            logger.LogInformation("Cloudflare: {Count} маршрутов", SplitRoutingConstants.CloudflareRoutes.Count);
        }

        if (settings.CustomDomains.Count > 0)
        {
            if (!processRunner.IsCommandAvailable("dig"))
                throw new WireGuardOperationException("Для резолва доменов нужен dig (bind-utils / dnsutils)");

            foreach (var domain in settings.CustomDomains)
            {
                var trimmed = domain.Trim();
                progress?.Report($"Progress_Resolve_Domain|{trimmed}");
                logger.LogInformation("Резолв домена {Domain}…", trimmed);
                var ips = await ResolveDomainAsync(trimmed, cancellationToken);
                foreach (var ip in ips)
                    routes.Add($"{ip}/32");
                logger.LogInformation(
                    "Домен {Domain}: {Count} адресов → {Routes}",
                    trimmed,
                    ips.Count,
                    ips.Count > 0 ? string.Join(", ", ips) : "—");
            }
        }

        if (settings.Youtube)
        {
            var cached = await LoadGoogleCacheAsync(cancellationToken).ConfigureAwait(false);
            foreach (var route in cached)
                routes.Add(route);

            if (cached.Count > 0)
            {
                progress?.Report($"Progress_Youtube_Cache|{cached.Count}");
                logger.LogInformation("Google/YouTube: {Count} маршрутов из кэша", cached.Count);
            }
            else
            {
                progress?.Report("Progress_Youtube_Download");
                logger.LogInformation("Загрузка Google/YouTube IP-диапазонов…");
                var googleRoutes = await FetchGoogleRoutesOnlineAsync(cancellationToken).ConfigureAwait(false);
                foreach (var route in googleRoutes)
                    routes.Add(route);
                logger.LogInformation("Google/YouTube: {Count} маршрутов", googleRoutes.Count);
            }
        }

        var result = routes
            .OrderBy(r => r, StringComparer.Ordinal)
            .Take(settings.MaxRoutes > 0 ? settings.MaxRoutes : 200)
            .ToList();

        if (result.Count < routes.Count)
            logger.LogWarning(
                "Маршрутов {Total}, лимит {Max} — обрезано до {Taken}",
                routes.Count,
                settings.MaxRoutes > 0 ? settings.MaxRoutes : 200,
                result.Count);

        logger.LogInformation("Итого маршрутов после сканирования: {Count}", result.Count);
        return result;
    }

    private async Task<IReadOnlyList<string>> FetchGoogleRoutesOnlineAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<GoogleIpRangesResponse>(
                SplitRoutingConstants.GoogleJsonUrl,
                cancellationToken);

            var routes = response?.Prefixes?
                .Where(p => !string.IsNullOrWhiteSpace(p.Ipv4Prefix))
                .Select(p => p.Ipv4Prefix!)
                .Distinct()
                .ToList() ?? [];

            if (routes.Count > 0)
                await SaveGoogleCacheAsync(routes, cancellationToken).ConfigureAwait(false);

            return routes;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось загрузить Google IP-диапазоны онлайн");
            return [];
        }
    }

    private static async Task SaveGoogleCacheAsync(
        IReadOnlyList<string> routes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GoogleCachePath)!);
        await File.WriteAllTextAsync(
            GoogleCachePath,
            JsonSerializer.Serialize(routes),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> LoadGoogleCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(GoogleCachePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(GoogleCachePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> ResolveDomainAsync(
        string domain,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return [];

        var result = await processRunner.RunAsync(
            "dig",
            ["+short", "A", domain],
            cancellationToken);

        if (!result.IsSuccess)
            return [];

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => IpV4Pattern().IsMatch(line.Trim()))
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}$")]
    private static partial Regex IpV4Pattern();

    private sealed class GoogleIpRangesResponse
    {
        [JsonPropertyName("prefixes")]
        public List<GooglePrefix>? Prefixes { get; set; }
    }

    private sealed class GooglePrefix
    {
        [JsonPropertyName("ipv4Prefix")]
        public string? Ipv4Prefix { get; set; }
    }
}
