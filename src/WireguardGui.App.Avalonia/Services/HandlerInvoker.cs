using Microsoft.Extensions.DependencyInjection;

namespace WireguardGui.App.Avalonia.Services;

internal sealed class HandlerInvoker(IServiceProvider serviceProvider)
{
    public async Task<T> InvokeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InvokeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }
}
