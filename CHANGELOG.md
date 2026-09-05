# Changelog

All notable changes to this project are documented in this file.

## [1.4.10] - 2026-09-05

### Fixed

- **Native backend connection status now updates after connect** (issue #1): `wg show` requires `CAP_NET_ADMIN`, so it silently failed with `Operation not permitted` and the status stayed `Unknown`. It now runs through the already-authorized pkexec session (`HasActivePrivilegedSession`) with no extra password prompt; falls back to the unprivileged path when no session exists (e.g. right after app start).

## [1.4.9] - 2026-09-05

### Fixed

- **App froze on startup with no window (regression in 1.4.8)**: circular DI dependency — the UI wrapper `SplitRoutingRefreshScheduler` resolved `ISplitRoutingRefreshScheduler`, which resolved back into the same wrapper. The container spun on `StackGuard` and hung the main thread before `window.Show()`. Removed the wrapper registration so `ISplitRoutingRefreshScheduler` resolves directly to the Application-layer `SplitRoutingRefreshService`.

## [1.4.8] - 2026-09-05

### Added

- Split routing hardening (audit round 2): config repository, Application-layer refresh service, and D-Bus DNS monitor.
- `WireGuardConfigRepository` — single locked access point for `wireguard.conf` (read/write/atomic update); eliminates races between parallel apply and save.
- `SplitRoutingRefreshService` moved from the UI layer into Application; UI supplies `ISplitRoutingTimer` (Avalonia `DispatcherTimer`) and `ISplitRoutingRefreshNotifier` (toasts) as ports. Background refresh no longer depends on presentation.
- `SystemdResolvedMonitorClient` (D-Bus `io.systemd.Resolve.Monitor`) is now the only DNS route monitor: no pkexec/FIFO, works on any systemd-resolved stack.
- Pretty test runner: `./test.sh` (Spectre.Console) prints `✅`/`❌` per test with a summary.

### Fixed

- Twitch GQL timeout crashes the app on repeat connect: `HttpClient.Timeout` throws `TaskCanceledException` (a subclass of `OperationCanceledException`) which slipped past both `is not OperationCanceledException` filters; it is now retried like a network error and real cancellations still propagate.
- `EnsureEndpointRoute` no longer hardcodes `dev home`/`dev wg` — only WireGuard interfaces (`wg show interfaces` + current profile iface) are excluded; warning when no non-tunnel default route candidate remains.
- `ApplySplitRoutingHandler` now calls `SplitRoutingSettings.Normalize()` (max routes clamp, domain dedupe) before collecting routes.
- Twitch discovery retries on HTTP 429/5xx honoring `Retry-After`; offline vs rate-limit vs network failures are logged distinctly.

### Changed

- `PolicyRoutingSetup` split into `IpRuleManager`, `NftSetManager`, `TunnelDnsManager`, `EndpointRouteGuard`, and `PolicyRoutingCommands` (orchestrator ~240 lines; behavior unchanged).
- `DomainRouteDnsProxy` documented as a fallback (not wired into DI) per explicit user decision.
- DNS route monitor parses systemd-resolved D-Bus frames (A/AAAA) instead of `resolvectl monitor` FIFO lines.

## [1.4.7] - 2026-09-04

### Added

- Split routing tooling probe for `ip`, `dig`, and `resolvectl`: UI banner + toast with install hints when tools are missing; warnings in logs if the DNS monitor or tunnel DNS are skipped.

## [1.4.6] - 2026-09-03

### Fixed

- Policy sync no longer flushes the whole routing table: merge dig routes, keep DNS-monitor `/32`/`/128` host rules so Twitch does not stall on refresh.
- Monitored host adds no longer rewrite the policy table default route on every batch.

## [1.4.5] - 2026-09-03

### Added

- Parallel systemd-resolved DNS monitor: privileged `resolvectl monitor` over the shared pkexec session; matched Twitch/YouTube/custom answers install batched `ip rule to <IP>/32`.
- After system resume (logind `PrepareForSleep`) and VPN reconnect, split routing is force-refreshed while connected.

### Fixed

- Do not point systemd-resolved at a local DNS proxy for all domains (that broke name resolution).
- Revert policy data-path to `ip rule to <CIDR>` — nft fwmark marking did not apply on this host, so Telegram/Twitch went direct.

### Changed

- Quiet Debug logs for routine privileged `ip rule` / `ip route` one-liners during sync.

## [1.4.4] - 2026-08-25

### Added

- Split routing: **Refresh routes** button force-rebuilds destination rules without toggling sources.

## [1.4.3] - 2026-08-25

### Fixed

- Twitch HLS discovery: updated PlaybackAccessToken GQL query and usher v2; parse SESSION-DATA / base64 hosts so CDN edges are routed (restores 1080p).
- Policy routing table id is stable across process restarts (SHA-256 instead of randomized `GetHashCode`); orphan `ip rule` tables are cleaned on apply.
- After sleep/refresh with unchanged routes: re-install table default route and tunnel DNS instead of no-oping.

## [1.4.2] - 2026-08-25

### Added

- Policy split routing: tunnel DNS field in the profile UI (default `8.8.8.8`); DNS queries go through the VPN via systemd-resolved on the WireGuard interface.
- Keep/write `DNS` in the WireGuard config for policy mode; sync profile DNS into `.conf` on save/apply/connect.

### Fixed

- Writing DNS into the profile `.conf` no longer eats newlines (previously glued lines and wiped `PrivateKey` / `[Peer]`).

## [1.4.1] - 2026-08-20

### Added

- Custom domains: CIDR entries (`157.240.0.0/16`) accepted as static routes; apex domains also resolve `www.`.

### Fixed

- Disconnect crash when policy routing table was already empty (`FIB table does not exist`).
- Apply/disconnect: unexpected policy-routing and resolve errors no longer crash the UI.

## [1.4.0] - 2026-08-19

### Added

- Policy-based split routing on Linux: selected destinations use `ip rule to <CIDR> lookup <table>` with a per-profile routing table instead of stuffing routes into `AllowedIPs`.
- `IPolicyRoutingSetup` / `PolicyRoutingSetup`: apply on connect, sync on refresh, teardown on disconnect.
- WireGuard policy baseline when `ip` is available: `Table = off`, `AllowedIPs = 0.0.0.0/0` (main table stays direct; only split targets go through VPN).
- Telegram domain DNS resolve and Twitch AAAA records for policy routing.
- Endpoint host `/32` route via the main gateway so the tunnel stays reachable.

### Changed

- Split routing refresh updates policy rules in place — no reconnect when CDN IPs change.
- Privileged network commands use `RunPrivilegedAsync` argument lists instead of bash scripts; full privileged script text logs at Debug only.

### Fixed

- Policy routing apply failing when the WireGuard interface has no IPv6 (IPv6 rules skipped when the tunnel is IPv4-only).

## [1.3.1] - 2026-08-18

### Added

- Settings: DNS refresh interval for split routing (`splitRoutingRefreshMinutes` in `settings.json`, 1–120 minutes).

### Fixed

- Settings page scrolls when content does not fit the window.

## [1.3.0] - 2026-08-02

### Added

- Twitch playlist/CDN seed hosts and optional channel field for HLS discovery via GQL + usher.
- Background split-routing refresh every 10 minutes while connected (reconnect only when AllowedIPs change).
- `TwitchStreamHostCache` persists discovered stream hosts between applies.
- Localization for Twitch channel / refresh toasts (7 languages).

### Changed

- Twitch domain normalizer skips known NXDOMAIN parents (`abs.hls.ttvnw.net`, `live-video.net`, …).
- Twitch route collection digs curated seeds plus cached/discovered stream hostnames.

### Fixed

- Stale CloudFront/Fastly `/32` routes after Twitch CDN IP rotation (refresh on connect/apply/timer).

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
