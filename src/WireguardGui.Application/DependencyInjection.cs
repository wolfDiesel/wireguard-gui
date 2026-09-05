using Microsoft.Extensions.DependencyInjection;
using WireguardGui.Application.Abstractions;
using WireguardGui.Application.Handlers;
using WireguardGui.Application.Services;

namespace WireguardGui.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWireguardGuiApplication(this IServiceCollection services)
    {
        services.AddSingleton<ImportProfileHandler>();
        services.AddSingleton<ConnectProfileHandler>();
        services.AddSingleton<DisconnectProfileHandler>();
        services.AddSingleton<DeleteProfileHandler>();
        services.AddSingleton<GetProfilesHandler>();
        services.AddSingleton<GetSystemCapabilitiesHandler>();
        services.AddSingleton<GetSplitRoutingToolingHandler>();
        services.AddSingleton<ApplySplitRoutingHandler>();
        services.AddSingleton<SaveProfileSplitRoutingHandler>();
        services.AddSingleton<GetProfileSplitRoutingHandler>();
        services.AddSingleton<GetSettingsHandler>();
        services.AddSingleton<SaveSettingsHandler>();
        services.AddSingleton<SplitRoutingRefreshService>();
        services.AddSingleton<ISplitRoutingRefreshScheduler>(
            sp => sp.GetRequiredService<SplitRoutingRefreshService>());
        return services;
    }
}
