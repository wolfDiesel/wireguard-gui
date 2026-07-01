# WireGuard GUI

[English](README.md) | [Русский](README.ru.md) | 中文

适用于 **Linux** 的 WireGuard 桌面客户端，基于 **Avalonia** 界面。管理 VPN 配置文件，通过 **NetworkManager**（`nmcli`）或 **原生** `wg-quick` 连接，并可将分流路由写入 `.conf` 文件中的 `AllowedIPs`。

## 系统要求

- **Linux**，需 GTK 3（可选 Ayatana AppIndicator 以使用系统托盘）。
- 主机上的 **WireGuard 后端**：
  - **NetworkManager** — `nmcli`、`network-manager`、WireGuard 插件；或
  - **原生** — `wg-quick`、`wireguard-tools`。
- 从源码构建需 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- 使用 NetworkManager 或 `wg-quick` 时，需 `pkexec`（PolicyKit）执行特权操作。

## 功能

- **配置文件管理** — 导入 `.conf`、列出配置、连接、断开、删除。
- **双后端** — 导入时可为每个配置选择 NetworkManager 或原生 `wg-quick`。
- **实时连接状态** — 每隔数秒轮询更新配置状态与状态栏。
- **分流路由（Split routing）** — 每个配置可选：
  - YouTube / Google（缓存或在线 IP 段），
  - Telegram，
  - Cloudflare CDN 段，
  - 自定义域名（DNS 解析 → 写入 `AllowedIPs` 的 CIDR）。
- **应用路由** — 扫描、写入 `AllowedIPs`；已连接时在 NetworkManager 中自动重连；长时间扫描时显示进度。
- **单次特权会话** — 一个 `pkexec` shell 批量执行 `nmcli` / `wg` 命令，避免反复输入密码。
- **Linux 桌面集成**
  - 系统托盘（显示 / 连接 / 断开 / 退出），基于 `libayatana-appindicator3`。
  - 最小化到托盘、关闭到托盘。
- **外观** — 深色 / 浅色 / 跟随系统主题，多种强调色。
- **本地化** — 英语（默认）、俄语、法语、德语、西班牙语、中文、日语；语言保存在 `~/.local/share/wireguard-gui/settings.json`。
- **AppImage** — CI 构建 x86_64 镜像，附于 [GitHub Releases](https://github.com/wolfDiesel/wireguard-gui/releases)。

## 从源码运行

```bash
git clone https://github.com/wolfDiesel/wireguard-gui.git
cd wireguard-gui
dotnet run --project src/WireguardGui.App.Avalonia/WireguardGui.App.Avalonia.csproj
```

**Fedora** 示例运行时包：

```bash
sudo dnf install gtk3 libayatana-appindicator3 wireguard-tools NetworkManager-wireguard
```

**Ubuntu/Debian** 见 `packaging/appimage/install-deps-ubuntu.sh`（CI 所用包）。

测试：

```bash
dotnet test
```

## 数据目录

应用数据位于 `~/.local/share/wireguard-gui/`：

- `profiles/` — 已导入的配置与元数据
- `settings.json` — 窗口、主题、托盘、语言
- `google-ipranges-cache.json` — YouTube/Google 分流路由缓存

## AppImage

从 [Releases](https://github.com/wolfDiesel/wireguard-gui/releases) 下载后：

```bash
chmod +x WireguardGui-*-x86_64.AppImage
./WireguardGui-*-x86_64.AppImage
```

本地构建：

```bash
./packaging/appimage/install-deps-ubuntu.sh
APPIMAGE_VERSION=0.1.0 ./packaging/appimage/build-appimage.sh
```

## 许可证

GPL-3.0-or-later — 见 [LICENSE](LICENSE)。
