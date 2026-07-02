using WireguardGui.Application.Abstractions;

namespace WireguardGui.Infrastructure.Storage;

public sealed class AppDataPaths : IAppDataPaths
{
    public string DataRoot { get; } = JsonProfileStore.GetDefaultDataRoot();
}
