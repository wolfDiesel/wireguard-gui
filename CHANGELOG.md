# Changelog

All notable changes to this project are documented in this file.

## [1.0.0] - 2026-07-01

### Added

- README in Russian (`README.ru.md`) and Chinese (`README.zh.md`); language links in main README.
- Localization unit tests (`LocalizationServiceTests`).

### Fixed

- Embedded localization JSON resources not loading at runtime (button labels showed keys like `Profiles_Import`).
- App crash when switching UI language with system tray enabled (GTK menu lifecycle / `g_object_ref_sink`).
- Language change handlers marshalled to the UI thread; safe `string.Format` for localized progress messages.
- Russian tray and backend strings (removed mixed English).

## [0.1.0] - 2026-07-01

### Added

- Avalonia desktop UI for WireGuard profile management on Linux.
- Import WireGuard `.conf` profiles (NetworkManager or native `wg-quick` backend).
- Connect, disconnect, delete profiles with live status polling.
- Split routing: YouTube/Google, Telegram, Cloudflare, custom domains via `AllowedIPs`.
- Apply routes with progress UI and auto-reconnect when connected via NetworkManager.
- Single long-lived privileged shell session (`pkexec`) for batched backend commands.
- System tray integration (Ayatana AppIndicator): show, connect, disconnect, quit.
- Theme settings: dark / light / system, accent palettes, tray behavior.
- Localization: English, Russian, French, German, Spanish, Chinese, Japanese.
- AppImage packaging and GitHub Actions release workflow.
- Unit tests for domain, application, and infrastructure layers.
