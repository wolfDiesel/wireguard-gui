# WireGuard GUI

English | [Русский](README.ru.md) | [中文](README.zh.md)

Desktop WireGuard client for **Linux** with an **Avalonia** UI. Manage VPN profiles, connect through **NetworkManager** (`nmcli`), **native** `wg-quick` or **AmneziaWG** (`awg-quick`), and configure **split routing** by writing routes into `AllowedIPs` in your `.conf` files.

## Requirements

- **Linux** with GTK 3 (optional Ayatana AppIndicator for the system tray).
- **Tunnel backend** on the host, any of:
  - **NetworkManager** — `nmcli`, `network-manager`, WireGuard plugin;
  - **Native** — `wg-quick`, `wireguard-tools`;
  - **AmneziaWG** — `awg-quick`, `amneziawg-tools` plus the `amneziawg` kernel module or `amneziawg-go`.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) when building from source.
- `pkexec` (PolicyKit) for privileged operations when using NetworkManager or `wg-quick`.

## Features

- **Profile management** — import `.conf` files, list profiles, connect, disconnect, delete.
- **Three backends** — NetworkManager, native `wg-quick` or AmneziaWG `awg-quick`, chosen per profile at import time; the backend is suggested from the config contents.
- **Live connection status** — polling updates profile state and the status bar every few seconds.
- **Split routing** — optional per-profile routing for:
  - YouTube / Google (from cached or online IP ranges),
  - Telegram,
  - Cloudflare CDN ranges,
  - custom domains (DNS resolve → CIDR in `AllowedIPs`).
- **Apply routes** — scan, write `AllowedIPs` to the config, auto-reconnect in NetworkManager when already connected; progress UI during long scans.
- **Dynamic DNS routes** — live host routes for Twitch/custom domains via systemd-resolved D-Bus monitor (no pkexec/FIFO).
- **Single privileged session** — one `pkexec` shell for batched `nmcli` / `wg` commands instead of repeated password prompts.
- **Linux desktop integration**
  - System tray (Show / Connect / Disconnect / Quit) via `libayatana-appindicator3`.
  - Minimize-to-tray and close-to-tray options.
- **Appearance** — dark / light / system theme, accent color palettes.
- **Localization** — English (default), Russian, French, German, Spanish, Chinese, Japanese; language stored in `~/.local/share/wireguard-gui/settings.json`.
- **AppImage** — x86_64 images built in CI and attached to [GitHub releases](https://github.com/wolfDiesel/wireguard-gui/releases).

## AmneziaWG

AmneziaWG is a WireGuard fork with traffic obfuscation. The Amnezia client exports its tunnels as
ordinary `.conf` files: import one and pick the **AmneziaWG** backend — obfuscation keys (`Jc`, `Jmin`,
`Jmax`, `S1`–`S4`, `H1`–`H4`, `I1`–`I5`, …) are kept as-is, only `AllowedIPs`/`Table`/`DNS` are edited.

Install the tools first (package names follow the upstream Amnezia guide — check them for your distribution):

```bash
# Fedora
sudo dnf copr enable amneziavpn/amneziawg
sudo dnf install amneziawg-tools amneziawg-dkms

# Debian/Ubuntu
sudo add-apt-repository ppa:amnezia/ppa
sudo apt install amneziawg amneziawg-tools
```

If the kernel module cannot be built for your kernel, install `amneziawg-go` instead: `awg-quick`
falls back to the userspace implementation automatically. NetworkManager cannot manage AmneziaWG
tunnels (it only knows `type wireguard`), so `nmcli` is not offered for these profiles.

### Install from the app

Open **Settings → Amnesia**. If the backend is already installed, the tab shows the tools version and
the transport (kernel module or `amneziawg-go`). Otherwise press **Check** to re-probe, **Install** to
install it, or **Manual install** for copy-paste instructions.

**Install** works on Debian/Ubuntu (PPA `amnezia/ppa`), RHEL/CentOS (COPR `amneziavpn/amneziawg`),
Fedora (tools and `amneziawg-go` built from source — the COPR project has no Fedora chroots) and
ALT Linux (distribution repository). It asks for administrator rights (polkit), then shows every step
with its output; the dialog stays open until it finishes. On an unknown distribution only the manual
instructions are offered.

## Run from source

```bash
git clone https://github.com/wolfDiesel/wireguard-gui.git
cd wireguard-gui
dotnet run --project src/WireguardGui.App.Avalonia/WireguardGui.App.Avalonia.csproj
```

On **Fedora** (example runtime packages):

```bash
sudo dnf install gtk3 libayatana-appindicator3 wireguard-tools NetworkManager-wireguard
```

On **Ubuntu/Debian**, see `packaging/appimage/install-deps-ubuntu.sh` for packages used in CI.

Tests:

```bash
dotnet test
```

Three test projects (`tests/*/*.Tests.csproj`) run in both CI contours — the `test` job in GitLab CI and `test` in GitHub Actions (`.github/workflows/ci.yml`) — and each publishes a JUnit report (`artifacts/test-results/*.xml`). The AppImage is built in GitHub Actions only.

## Data directory

Application data lives under `~/.local/share/wireguard-gui/`:

- `profiles/` — imported configs and metadata
- `settings.json` — window, theme, tray, language
- `google-ipranges-cache.json` — cached YouTube/Google routes for split routing

## AppImage

Download from [Releases](https://github.com/wolfDiesel/wireguard-gui/releases), then:

```bash
chmod +x WireguardGui-*-x86_64.AppImage
./WireguardGui-*-x86_64.AppImage
```

Build locally:

```bash
./packaging/appimage/install-deps-ubuntu.sh
APPIMAGE_VERSION=0.1.0 ./packaging/appimage/build-appimage.sh
```

## License

GPL-3.0-or-later — see [LICENSE](LICENSE).
