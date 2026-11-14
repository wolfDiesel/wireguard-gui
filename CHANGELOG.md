# Changelog

All notable changes to this project are documented in this file.

## [1.5.0] - 2026-11-14

### Added

- **AmneziaWG tunnels (third backend).** `.conf` files exported by the Amnezia client (WireGuard with
  traffic obfuscation) can now be imported and managed like any other profile: the obfuscation keys
  (`Jc`, `Jmin`, `Jmax`, `S1`–`S4`, `H1`–`H4`, `I1`–`I5`, …) are kept verbatim, while split routing keeps
  rewriting only `AllowedIPs`/`Table`/`DNS`. The import dialog got a third backend (`awg-quick`), the
  config kind is detected before the dialog so an AmneziaWG file preselects it, and importing such a
  file into native/nmcli is rejected with an explanation instead of producing a broken profile. When
  the tools are missing, the dialog lists what to install (`amneziawg-tools` plus the `amneziawg`
  kernel module or `amneziawg-go`) and blocks the import. Tunnel state reads in split routing
  (endpoint route guard, handshake probe) now pick `awg` or `wg` by backend, because `wg show` cannot
  see AmneziaWG interfaces.

- **AmneziaWG backend can be installed from Settings.** The new **Settings → Amnesia** tab shows whether
  the backend is usable (tools version and transport: kernel module or `amneziawg-go`) and, when it is
  missing, offers **Check**, **Install** and **Manual install**. Install detects the distribution from
  `/etc/os-release` and runs fixed plans: Debian/Ubuntu (PPA) and RHEL/CentOS (COPR) install packages,
  ALT Linux installs from its repository, and Fedora builds `amneziawg-tools` plus the userspace
  `amneziawg-go` from source because the COPR project has no Fedora chroots. Everything goes through
  the existing pkexec session, with a modal that shows every step and its output; manual mode gives
  copy-paste commands for those families plus building the DKMS module from source. Unknown
  distributions get manual instructions only, and every plan ends with a tool check
  (`command -v awg awg-quick`), so a partial install is reported as a failure instead of a silent
  "installed".

## [1.4.16] - 2026-10-25

### Fixed

- **Split DNS stayed pointed at a dead tunnel, so whole name groups stopped resolving.** After a
  resume, a provider outage or a VPN-server failure the WireGuard interface stays "activated" in
  NetworkManager while no handshake completes. `resolvectl dns <iface> 8.8.8.8` and the split-routing
  domains stayed in place, so every name from `DnsRouteSuffixes.ForTunnelDns` (youtube, twitch, t.me,
  custom domains…) timed out for seconds and resolved nowhere, even though `/etc/resolv.conf` pointed
  at the systemd-resolved stub. `TunnelDnsManager` now probes the tunnel first (`TunnelHealthProbe`:
  interface presence plus `wg show <iface> latest-handshakes`, a handshake older than five minutes
  counts as dead) and, when the tunnel cannot answer, reverts the link DNS (`resolvectl revert`),
  removes the `ip rule` for the tunnel DNS server, flushes the resolver cache and logs a warning —
  split domains keep resolving through the local resolver instead of hanging. The withheld state is
  exposed via `IPolicyRoutingSetup.IsSplitDnsDegraded`, so the background refresh keeps retrying and
  restores split DNS as soon as a handshake is back, even for profiles without dynamic sources.

- **Destination `ip rule`s accumulated until they hijacked unrelated addresses.** Rules installed by
  the DNS-route monitor were never forgotten: the tracked host set only grew, every refresh added the
  addresses of freshly resolved hosts and nothing removed the stale ones, so a long-lived profile
  ended up with ~1000 rules — including the tuna entry point `45.129.128.77`, which sent
  `danotez.a.2na.am` / `multitwatch.a.2na.am` traffic into the dead tunnel. Rules are now reconciled
  against the current state on every sync/refresh (`IpRuleManager.PruneDestinationRulesAsync`), and
  the monitored-host set is capped by `maxRoutes` with the oldest dynamic entries evicted; the static
  routes of the profile are never evicted.

- **Split domains were resolved outside the tunnel.** Route collection used plain `dig` through the
  system resolver, i.e. the local (possibly filtered) DNS chain. On networks where the provider or
  the registry answers `NXDOMAIN` for a whole name (e.g. apex `rutracker.org`, `kinozal.me` while
  `www.*` still resolve), custom domains produced zero `/32` routes and the apply reported success
  without a warning; only domains already covered by another rule (Cloudflare ranges, the DNS-route
  monitor) still reached the tunnel. `DomainDnsResolver` now queries the tunnel DNS servers from
  `SplitRoutingSettings.TunnelDns` (`dig +short +time=2 +tries=1 … @<server>`, servers tried in
  order) for the domains listed in the profile, and falls back to the system resolver only when the
  tunnel DNS returns no A/AAAA answer. A custom domain that resolves nowhere is logged as a warning
  instead of silently yielding no routes. The set of tunnelled names and the rest of the pipeline
  are unchanged: only the names configured in the profile are resolved through the tunnel.

## [1.4.15] - 2026-10-23

### Fixed

- **Domains with a short TTL stalled while connected (e.g. `*.tuna.am`)**: policy mode used
  `resolvectl domain <iface> ~.` and sent *every* system DNS query through the tunnel to
  `8.8.8.8`. With a remote resolver (~50–100 ms per uncached lookup, occasional timeouts) names
  with a ~30 s TTL were re-resolved almost constantly and hung for seconds; each route refresh
  added ~60 more queries to the same path. Tunnel DNS now applies routing domains only to
  split-routing suffixes (`DnsRouteSuffixes.ForTunnelDns`: Twitch/YouTube/custom plus
  `telegram.org`/`t.me`), so all other names keep using the local resolver. The tunnel link is
  also removed as the resolved default route (`resolvectl default-route <iface> no`), otherwise
  systemd-resolved could still answer non-split names (e.g. `*.tuna.am`) from `8.8.8.8` through
  the tunnel and hand out a geographically distant CDN edge.
- **Live DNS-route monitor was dead after 1.4.8 where polkit denies the D-Bus subscription**
  (`io.systemd.InteractiveAuthenticationRequired`): the monitor logged a warning every minute and
  dynamic `/32` routes for Twitch/custom domains were never installed. It now falls back to the
  privileged `resolvectl monitor` (FIFO over the shared pkexec session, as in ≤1.4.7) and only
  defers quietly while no privileged session is active.

- **Split-routing refresh never started when the app was launched while the tunnel was already
  up**: the profile list only reported a `disconnected → connected` transition to the scheduler, so
  a profile that was already connected at startup never got its refresh timer or DNS-route monitor.
  The poller now starts watching every connected profile with split routing enabled (and stops when
  it disconnects or splits get disabled), and the first poll runs during startup.

## [1.4.14] - 2026-09-25

### Fixed

- **YouTube video stalled on full route sets**: when YouTube + Telegram + Twitch + Cloudflare + custom domains together exceeded `maxRoutes` (hard-capped at 200), the YouTube source (lowest priority) was truncated first and dropped the video CDN range `74.125.0.0/16`, so the watch page loaded but video playback did not. Raised `DefaultMaxRoutes` to 1000 and `MaxMaxRoutes` to 2000; the route-limit toast now shows the actual limit instead of a hard-coded "200".

## [1.4.13] - 2026-09-08

### Fixed

- **Toasts no longer pile up / disappear automatically**: toasts shown from a background thread (e.g. split-routing refresh) created their `DispatcherTimer` on a non-UI thread, so the timeout tick never ran and toasts stuck around forever. `AppToastService.Show`/`Dismiss` now force execution onto the UI dispatcher. Hovering a toast pauses its timer; leaving restarts the full 4 s timeout.

## [1.4.12] - 2026-09-05

### Fixed

- **Privileged session broken with `Exec format error` (exit 127)**: the helper script file started with an empty line — C# raw string literal adds a leading `\n` after `"""`, so `#!/bin/bash` was on the second line and the kernel refused to `execve` it (ENOEXEC). The helper is now launched as `pkexec bash <script>` — bash reads the file itself, no shebang needed.
- **Internal interface name lookup**: `nmcli -g wireguard.interface` is not a valid field (nmcli rejects it); replaced with `connection.interface-name`.

## [1.4.11] - 2026-09-05

### Added

- **About page with app version**: new "About" page in the left nav (Profiles / Settings / About) showing app name, logo, and build version. The version flows from the release tag (`APPIMAGE_VERSION` → `-p:AppVersion` → embedded `version.txt` → `AppVersionService`); dev and CI builds without a tag show "dev build" instead of `0.0.0`.
- `AppVersionService` — reads the embedded `WireguardGui.App.version.txt` resource (fallback `dev`); registered as a singleton.
- Localized keys for the About page (en, ru, de, fr, es, ja, zh).
- `AppVersionServiceTests` — embedded resource read, no `+`/`-` commit-suffix, stability across calls.

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
