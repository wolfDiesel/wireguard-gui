using WireguardGui.Domain;

namespace WireguardGui.Application.Abstractions;

/// <summary>
/// Единый репозиторий файла wireguard.conf профиля.
/// Все чтения/записи конфига идут через него, чтобы исключить гонки
/// между параллельными apply и save (пункт 6 анализа).
/// </summary>
public interface IWireGuardConfigRepository
{
    /// <summary>Читает текущее содержимое конфига профиля.</summary>
    Task<string> ReadAsync(VpnProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Полностью перезаписывает конфиг профиля.</summary>
    Task WriteAsync(VpnProfile profile, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно читает конфиг, применяет <paramref name="transform"/> и записывает результат,
    /// если он изменился. Возвращает итоговое содержимое.
    /// </summary>
    Task<string> UpdateAsync(
        VpnProfile profile,
        Func<string, string> transform,
        CancellationToken cancellationToken = default);
}