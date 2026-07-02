# Changelog

All notable changes to this project are documented in this file.

## [1.2.0] - 2026-07-01

### Added

- Localized operation errors: `OperationErrorCode`, `OperationErrorMapper`, and `Error_*` keys (7 languages).
- UI hint when split routing removes DNS from profile config (`Profiles_Split_Hint_DnsRemoved`).
- `GetProfileSplitRoutingHandler` — split routing settings loaded via application layer, not from ViewModels.
- `SplitRoutingPanelViewModel` and `ProfileListSynchronizer` — slimmer `ProfilesViewModel`.
- Typed `SplitRoutingProgress` for apply-routes progress (replaces pipe-delimited string protocol).
- `IProfileImporter`, `IAppDataPaths`, `ConnectionOutcomeResolver`, `VpnProfileNaming` validation.
- Parallel split-route source collection with bounded DNS resolve; deterministic route truncation by source priority.
- Unit tests expanded to 46 (handlers, backends, store migration, config parser edge cases).

### Changed

- `ConnectProfileHandler` skips backend reimport when split-routing config is unchanged.
- `JsonProfileStore` read path has no side effects; profile migration runs on save/list only.
- `DeleteProfileHandler` best-effort: removes profile files even if NetworkManager unregister fails.
- Native backend parses `wg show` by exact `interface:` column match.
- Handlers registered as singletons; debug logging enabled only in `#if DEBUG` builds.
- Removed unused torrent launch dead code and unused `IWireGuardBackend.ImportAsync` / `ApplyRoutesAsync`.

### Fixed

- Legacy profiles without `IncludeCloudflare` in JSON default to `false` on load.
- `SplitRoutingSettings.Normalize()` clamps `MaxRoutes` and deduplicates custom domains.

## [1.1.0] - 2026-07-01

### Added

- Twitch split routing: per-profile checkbox with curated domain list and DNS resolve to `AllowedIPs`.
- `ISplitRouteSource` architecture: separate route collectors for YouTube, Telegram, Twitch, Cloudflare, and custom domains.
- `TwitchDomainNormalizer` for wildcard domain patterns before DNS lookup.
- Localization key `Toast_Split_Saved` (7 languages).

### Changed

- `SplitRouteBuilder` orchestrates registered route sources instead of inline logic.
- All log messages and in-code error strings use English (default project language).

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
