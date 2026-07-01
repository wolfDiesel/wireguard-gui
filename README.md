# WireGuard GUI

English | [Русский](README.ru.md) | [中文](README.zh.md)

Desktop WireGuard client for **Linux** with an **Avalonia** UI. Manage VPN profiles, connect through **NetworkManager** (`nmcli`) or **native** `wg-quick`, and configure **split routing** by writing routes into `AllowedIPs` in your `.conf` files.

## Requirements

- **Linux** with GTK 3 (optional Ayatana AppIndicator for the system tray).
- **WireGuard backend** on the host:
  - **NetworkManager** — `nmcli`, `network-manager`, WireGuard plugin; or
  - **Native** — `wg-quick`, `wireguard-tools`.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) when building from source.
- `pkexec` (PolicyKit) for privileged operations when using NetworkManager or `wg-quick`.

## Features

- **Profile management** — import `.conf` files, list profiles, connect, disconnect, delete.
- **Dual backend** — choose NetworkManager or native `wg-quick` per profile at import time.
- **Live connection status** — polling updates profile state and the status bar every few seconds.
- **Split routing** — optional per-profile routing for:
  - YouTube / Google (from cached or online IP ranges),
  - Telegram,
  - Cloudflare CDN ranges,
  - custom domains (DNS resolve → CIDR in `AllowedIPs`).
- **Apply routes** — scan, write `AllowedIPs` to the config, auto-reconnect in NetworkManager when already connected; progress UI during long scans.
- **Single privileged session** — one `pkexec` shell for batched `nmcli` / `wg` commands instead of repeated password prompts.
- **Linux desktop integration**
  - System tray (Show / Connect / Disconnect / Quit) via `libayatana-appindicator3`.
  - Minimize-to-tray and close-to-tray options.
- **Appearance** — dark / light / system theme, accent color palettes.
- **Localization** — English (default), Russian, French, German, Spanish, Chinese, Japanese; language stored in `~/.local/share/wireguard-gui/settings.json`.
- **AppImage** — x86_64 images built in CI and attached to [GitHub releases](https://github.com/wolfDiesel/wireguard-gui/releases).

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
