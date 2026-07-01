using Microsoft.Extensions.DependencyInjection;
using WireguardGui.Application.Abstractions;
using WireguardGui.Infrastructure.SplitRouting;
using WireguardGui.Infrastructure.Storage;
using WireguardGui.Infrastructure.System;
using WireguardGui.Infrastructure.WireGuard;

namespace WireguardGui.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWireguardGuiInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<ISplitRouteBuilder, SplitRouteBuilder>(client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISystemCapabilityProbe, SystemCapabilityProbe>();
        services.AddSingleton<IProfileStore, JsonProfileStore>();
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
