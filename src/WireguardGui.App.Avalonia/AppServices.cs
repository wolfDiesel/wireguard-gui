using Microsoft.Extensions.DependencyInjection;
using WireguardGui.App.Avalonia.Services;
using WireguardGui.Application;

namespace WireguardGui.App.Avalonia;

internal static class AppServices
{
    private static IServiceProvider _provider = null!;

    public static void Initialize(IServiceProvider provider) => _provider = provider;

    public static T GetRequired<T>() where T : notnull => _provider.GetRequiredService<T>();
}
