using Microsoft.Extensions.DependencyInjection;
using WireguardGui.Application.Handlers;

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
        services.AddSingleton<ApplySplitRoutingHandler>();
        services.AddSingleton<SaveProfileSplitRoutingHandler>();
        services.AddSingleton<GetProfileSplitRoutingHandler>();
        services.AddSingleton<GetSettingsHandler>();
        services.AddSingleton<SaveSettingsHandler>();
        return services;
    }
}
