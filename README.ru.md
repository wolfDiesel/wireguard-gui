# WireGuard GUI

[English](README.md) | Русский | [中文](README.zh.md)

Настольный клиент WireGuard для **Linux** с интерфейсом на **Avalonia**. Управление VPN-профилями, подключение через **NetworkManager** (`nmcli`) или **нативный** `wg-quick`, настройка **split routing** с записью маршрутов в `AllowedIPs` в файлах `.conf`.

## Требования

- **Linux** с GTK 3 (опционально Ayatana AppIndicator для системного трея).
- **Бэкенд WireGuard** на хосте:
  - **NetworkManager** — `nmcli`, `network-manager`, плагин WireGuard; или
  - **Native** — `wg-quick`, `wireguard-tools`.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) для сборки из исходников.
- `pkexec` (PolicyKit) для привилегированных операций при использовании NetworkManager или `wg-quick`.

## Возможности

- **Профили** — импорт `.conf`, список профилей, подключение, отключение, удаление.
- **Два бэкенда** — NetworkManager или нативный `wg-quick` выбирается при импорте профиля.
- **Статус подключения** — опрос состояния профилей и строки статуса каждые несколько секунд.
- **Split routing** — опциональная маршрутизация по профилю:
  - YouTube / Google (из кэша или онлайн-диапазонов IP),
  - Telegram,
  - диапазоны Cloudflare CDN,
  - пользовательские домены (DNS → CIDR в `AllowedIPs`).
- **Применение маршрутов** — сканирование, запись `AllowedIPs` в конфиг, автопереподключение в NetworkManager при активном VPN; индикатор прогресса при длительном сканировании.
- **Одна привилегированная сессия** — один `pkexec`-shell для пакетных команд `nmcli` / `wg` вместо повторных запросов пароля.
- **Интеграция с рабочим столом Linux**
  - Системный трей (Показать / Подключить / Отключить / Выход) через `libayatana-appindicator3`.
  - Сворачивание и закрытие в трей.
- **Оформление** — тёмная / светлая / системная тема, палитры акцентного цвета.
- **Локализация** — английский (по умолчанию), русский, французский, немецкий, испанский, китайский, японский; язык хранится в `~/.local/share/wireguard-gui/settings.json`.
- **AppImage** — сборки x86_64 в CI, прикрепляются к [релизам на GitHub](https://github.com/wolfDiesel/wireguard-gui/releases).

## Запуск из исходников

```bash
git clone https://github.com/wolfDiesel/wireguard-gui.git
cd wireguard-gui
dotnet run --project src/WireguardGui.App.Avalonia/WireguardGui.App.Avalonia.csproj
```

На **Fedora** (пример пакетов):

```bash
sudo dnf install gtk3 libayatana-appindicator3 wireguard-tools NetworkManager-wireguard
```

На **Ubuntu/Debian** — см. `packaging/appimage/install-deps-ubuntu.sh` (пакеты из CI).

Тесты:

```bash
dotnet test
```

## Каталог данных

Данные приложения: `~/.local/share/wireguard-gui/`:

- `profiles/` — импортированные конфиги и метаданные
- `settings.json` — окно, тема, трей, язык
- `google-ipranges-cache.json` — кэш маршрутов YouTube/Google для split routing

## AppImage

Скачайте из [Releases](https://github.com/wolfDiesel/wireguard-gui/releases), затем:

```bash
chmod +x WireguardGui-*-x86_64.AppImage
./WireguardGui-*-x86_64.AppImage
```

Локальная сборка:

```bash
./packaging/appimage/install-deps-ubuntu.sh
APPIMAGE_VERSION=0.1.0 ./packaging/appimage/build-appimage.sh
```

## Лицензия

GPL-3.0-or-later — см. [LICENSE](LICENSE).
