namespace WireguardGui.Application.Abstractions;

public interface ISettingsStore
{
    Task<Domain.AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Domain.AppSettings settings, CancellationToken cancellationToken = default);
}
