using Microsoft.Extensions.DependencyInjection;
using WireguardGui.Application.Handlers;

namespace WireguardGui.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddWireguardGuiApplication(this IServiceCollection services)
    {
        services.AddScoped<ImportProfileHandler>();
        services.AddScoped<ConnectProfileHandler>();
        services.AddScoped<DisconnectProfileHandler>();
        services.AddScoped<DeleteProfileHandler>();
        services.AddScoped<GetProfilesHandler>();
        services.AddScoped<GetSystemCapabilitiesHandler>();
        services.AddScoped<ApplySplitRoutingHandler>();
        services.AddScoped<SaveProfileSplitRoutingHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<SaveSettingsHandler>();
        return services;
    }
}
