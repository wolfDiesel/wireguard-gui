using Microsoft.Extensions.DependencyInjection;
using WireguardGui.App.Avalonia.Converters;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.ViewModels;
using WireguardGui.Application;
using WireguardGui.Infrastructure;

namespace WireguardGui.App.Avalonia.Services;

internal static class ServiceRegistration
{
    public static IServiceCollection AddAvaloniaApp(this IServiceCollection services)
    {
        services.AddWireguardGuiApplication();
        services.AddWireguardGuiInfrastructure();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<DesktopSessionBridge>();
        services.AddSingleton<HandlerInvoker>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppToastService>();
        services.AddSingleton<StatusBarService>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }

    public static void WireLocalization(LocalizationService localization)
    {
        BoolToOnOffConverter.Instance.Localization = localization;
        localization.Changed += (_, _) => BoolToOnOffConverter.Instance.Localization = localization;
    }
}
