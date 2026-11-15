# WireGuard GUI

[English](README.md) | [Русский](README.ru.md) | 中文

适用于 **Linux** 的 WireGuard 桌面客户端，基于 **Avalonia** 界面。管理 VPN 配置文件，通过 **NetworkManager**（`nmcli`）、**原生** `wg-quick` 或 **AmneziaWG**（`awg-quick`）连接，并可将分流路由写入 `.conf` 文件中的 `AllowedIPs`。

## 系统要求

- **Linux**，需 GTK 3（可选 Ayatana AppIndicator 以使用系统托盘）。
- 主机上的 **隧道后端**，任选其一：
  - **NetworkManager** — `nmcli`、`network-manager`、WireGuard 插件；
  - **原生** — `wg-quick`、`wireguard-tools`；
  - **AmneziaWG** — `awg-quick`、`amneziawg-tools`，以及内核模块 `amneziawg` 或 `amneziawg-go`。
- 从源码构建需 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- 使用 NetworkManager、`wg-quick` 或 AmneziaWG 时，需 `pkexec`（PolicyKit）执行特权操作。

## 功能

- **配置文件管理** — 导入 `.conf`、列出配置、连接、断开、删除。
- **三种后端** — NetworkManager、原生 `wg-quick` 或 AmneziaWG `awg-quick`，导入时为每个配置选择；后端会根据配置文件内容给出建议。
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

## AmneziaWG

AmneziaWG 是带流量混淆的 WireGuard 分支。Amnezia 客户端把这类隧道导出为普通 `.conf` 文件：导入该文件并选择 **AmneziaWG** 后端——混淆参数（`Jc`、`Jmin`、`Jmax`、`S1`–`S4`、`H1`–`H4`、`I1`–`I5`，……）原样保留，只修改 `AllowedIPs`/`Table`/`DNS`。

请先安装工具（包名以 Amnezia 官方说明为准，并针对你的发行版核对）：

```bash
# Fedora
sudo dnf copr enable amneziavpn/amneziawg
sudo dnf install amneziawg-tools amneziawg-dkms

# Debian/Ubuntu
sudo add-apt-repository ppa:amnezia/ppa
sudo apt install amneziawg amneziawg-tools
```

如果内核模块无法针对你的内核编译，请改装 `amneziawg-go`：`awg-quick` 会自动回退到用户空间实现。NetworkManager 无法管理 AWG 隧道（它只认识 `type wireguard`），因此这类配置不提供 `nmcli`。

### 从应用安装

打开 **设置 → Amnesia**。如果后端已安装，该选项卡会显示工具版本和传输方式（内核模块或 `amneziawg-go`）；否则可按 **检查** 重新探测、按 **安装** 进行安装，或按 **手动安装** 获取可直接复制的命令。

**安装** 支持 Debian/Ubuntu（PPA `amnezia/ppa`）、RHEL/CentOS（COPR `amneziavpn/amneziawg`）、Fedora（从源码编译 `amneziawg-tools` 和 `amneziawg-go`——COPR 项目没有 Fedora chroot）以及 ALT Linux（发行版仓库）。安装时会请求管理员权限（polkit），随后显示每个步骤及其输出；窗口在完成前不会关闭。未知发行版只提供手动安装说明。

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

三个测试项目（`tests/*/*.Tests.csproj`）在两个 CI 中都会运行——GitLab CI 的 `test` job 与 GitHub Actions 的 `test`（`.github/workflows/ci.yml`）；每个项目都会生成 JUnit 报告（`artifacts/test-results/*.xml`）并作为构建产物上传。AppImage 仅在 GitHub Actions 中构建。

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
