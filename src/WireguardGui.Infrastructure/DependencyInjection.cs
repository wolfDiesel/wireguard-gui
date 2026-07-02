using Microsoft.Extensions.DependencyInjection;
using WireguardGui.Application.Abstractions;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.SplitRouting.Sources;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.System;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWireguardGuiInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppDataPaths, AppDataPaths>();
        services.AddSingleton<DomainDnsResolver>();
        // ISplitRouteSource order: lower Priority value = higher route priority when truncating.
        services.AddSingleton<ISplitRouteSource, TelegramSplitRouteSource>();
        services.AddSingleton<ISplitRouteSource, CloudflareSplitRouteSource>();
        services.AddSingleton<ISplitRouteSource, CustomDomainsSplitRouteSource>();
        services.AddSingleton<ISplitRouteSource, TwitchSplitRouteSource>();
        services.AddHttpClient<YouTubeSplitRouteSource>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<ISplitRouteSource>(sp => sp.GetRequiredService<YouTubeSplitRouteSource>());
        services.AddSingleton<ISplitRouteBuilder, SplitRouteBuilder>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISystemCapabilityProbe, SystemCapabilityProbe>();
        services.AddSingleton<IProfileStore, JsonProfileStore>();
        services.AddSingleton<IProfileImporter, ProfileImporter>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IWireGuardConfigValidator, WireGuardConfigValidator>();
        services.AddSingleton<IWireGuardConfigParser, WireGuardConfigParser>();
        services.AddSingleton<ISplitRoutingConfigUpdater, SplitRoutingConfigUpdater>();
        services.AddSingleton<NativeWireGuardBackend>();
        services.AddSingleton<NmcliWireGuardBackend>();
        services.AddSingleton<IWireGuardBackendFactory, WireGuardBackendFactory>();
        return services;
    }
}
