# WireGuard GUI

[English](README.md) | Русский | [中文](README.zh.md)

Настольный клиент WireGuard для **Linux** с интерфейсом на **Avalonia**. Управление VPN-профилями, подключение через **NetworkManager** (`nmcli`), **нативный** `wg-quick` или **AmneziaWG** (`awg-quick`), настройка **split routing** с записью маршрутов в `AllowedIPs` в файлах `.conf`.

## Требования

- **Linux** с GTK 3 (опционально Ayatana AppIndicator для системного трея).
- **Бэкенд туннеля** на хосте, любой из:
  - **NetworkManager** — `nmcli`, `network-manager`, плагин WireGuard;
  - **Native** — `wg-quick`, `wireguard-tools`;
  - **AmneziaWG** — `awg-quick`, `amneziawg-tools` плюс модуль ядра `amneziawg` или `amneziawg-go`.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) для сборки из исходников.
- `pkexec` (PolicyKit) для привилегированных операций при использовании NetworkManager или `wg-quick`.

## Возможности

- **Профили** — импорт `.conf`, список профилей, подключение, отключение, удаление.
- **Три бэкенда** — NetworkManager, нативный `wg-quick` или AmneziaWG `awg-quick`, выбирается при импорте профиля; бэкенд подсказывается по содержимому конфига.
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

## AmneziaWG

AmneziaWG — форк WireGuard с обфускацией трафика. Клиент Amnezia экспортирует такие туннели обычным
`.conf`: импортируйте файл и выберите бэкенд **AmneziaWG** — ключи обфускации (`Jc`, `Jmin`, `Jmax`,
`S1`–`S4`, `H1`–`H4`, `I1`–`I5`, …) сохраняются как есть, правятся только `AllowedIPs`/`Table`/`DNS`.

Сначала поставьте утилиты (имена пакетов — по инструкции Amnezia, проверьте для своего дистрибутива):

```bash
# Fedora
sudo dnf copr enable amneziavpn/amneziawg
sudo dnf install amneziawg-tools amneziawg-dkms

# Debian/Ubuntu
sudo add-apt-repository ppa:amnezia/ppa
sudo apt install amneziawg amneziawg-tools
```

Если модуль ядра не собирается под ваше ядро, поставьте `amneziawg-go`: `awg-quick` сам откатывается
на userspace-реализацию. NetworkManager не умеет управлять AWG-туннелями (знает только
`type wireguard`), поэтому для таких профилей `nmcli` не предлагается.

### Установка из приложения

Откройте **Настройки → Amnesia**. Если бэкенд уже установлен, вкладка показывает версию инструментов
и транспорт (модуль ядра или `amneziawg-go`). Иначе доступны **Проверить** (повторная проба),
**Установить** и **Установить вручную** (готовые команды для копирования).

**Установить** работает на Debian/Ubuntu (PPA `amnezia/ppa`), RHEL/CentOS (COPR `amneziavpn/amneziawg`),
Fedora (сборка `amneziawg-tools` и `amneziawg-go` из исходников — у COPR нет Fedora-chroot'ов) и
ALT Linux (репозиторий дистрибутива). Приложение запросит права администратора (polkit), затем
покажет каждый шаг и его вывод; окно не закрывается до завершения. На неизвестном дистрибутиве
доступна только ручная инструкция.

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

Три тестовых проекта (`tests/*/*.Tests.csproj`) гоняют оба контура CI — job `test` в GitLab CI и `test` в GitHub Actions (`.github/workflows/ci.yml`); каждый пишет отчёт JUnit (`artifacts/test-results/*.xml`) и отдаёт его артефактом. AppImage собирается только в GitHub Actions.

Красивый вывод с зелёными галочками (Spectre.Console, парсинг TRX):

```bash
./test.sh
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
