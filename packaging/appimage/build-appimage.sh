#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PKG="$ROOT/packaging/appimage"
BUILD="$ROOT/build/appimage"
APPDIR="$BUILD/AppDir"
PUBLISH="$BUILD/publish"
TOOLS="$BUILD/tools"
APP_BIN_DIR="$APPDIR/usr/bin"

export ARCH="${ARCH:-x86_64}"
APPIMAGE_VERSION="${APPIMAGE_VERSION:-0.0.0}"
OUTPUT_NAME="${OUTPUT_NAME:-WireguardGui-${APPIMAGE_VERSION}-x86_64.AppImage}"
export APPIMAGE_EXTRACT_AND_RUN=1

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required command: $1" >&2
    exit 1
  }
}

ensure_tools() {
  mkdir -p "$TOOLS"
  local deploy="$TOOLS/linuxdeploy-${ARCH}.AppImage"
  local appimagetool="$TOOLS/appimagetool-${ARCH}.AppImage"
  if [ ! -f "$deploy" ]; then
    curl -fsSL -o "$deploy" \
      "https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-${ARCH}.AppImage"
    chmod +x "$deploy"
  fi
  if [ ! -f "$appimagetool" ]; then
    curl -fsSL -o "$appimagetool" \
      "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-${ARCH}.AppImage"
    chmod +x "$appimagetool"
  fi
  export LINUXDEPLOY="$deploy"
  export APPIMAGETOOL="$appimagetool"
}

prepare_icon() {
  local icon="$BUILD/wireguard-gui.png"
  mkdir -p "$BUILD"
  if [ -f "$icon" ] && [ ! "$PKG/wireguard-gui.svg" -nt "$icon" ]; then
    echo "$icon"
    return
  fi
  if command -v rsvg-convert >/dev/null 2>&1; then
    rsvg-convert -w 512 -h 512 "$PKG/wireguard-gui.svg" -o "$icon"
  else
    echo "Install librsvg2-tools (rsvg-convert) to render the icon." >&2
    exit 1
  fi
  echo "$icon"
}

publish_app() {
  echo "Publishing Avalonia app (linux-x64, self-contained)..."
  rm -rf "$PUBLISH"
  dotnet publish "$ROOT/src/WireguardGui.App.Avalonia/WireguardGui.App.Avalonia.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "$PUBLISH"
}

assemble_appdir() {
  echo "Assembling AppDir..."
  rm -rf "$APPDIR"
  mkdir -p "$APP_BIN_DIR"
  cp -a "$PUBLISH/." "$APP_BIN_DIR/"
  rm -f \
    "$APP_BIN_DIR/createdump" \
    "$APP_BIN_DIR/libcoreclrtraceptprovider.so" \
    "$APP_BIN_DIR/libmscordbi.so" \
    "$APP_BIN_DIR/libmscordaccore.so"
  find "$APP_BIN_DIR" -name '*.so' -exec chmod -x {} +
  chmod +x "$APP_BIN_DIR/WireguardGui.App"
}

run_linuxdeploy() {
  local icon="$1"
  local main_bin="$APP_BIN_DIR/WireguardGui.App"
  "$LINUXDEPLOY" --appdir="$APPDIR" \
    --executable="$main_bin" \
    --desktop-file="$PKG/wireguard-gui.desktop" \
    --icon-file="$icon"

  local out="$ROOT/dist/$OUTPUT_NAME"
  mkdir -p "$ROOT/dist"
  rm -f "$out"
  "$APPIMAGETOOL" "$APPDIR" "$out"
  chmod +x "$out"
  echo "AppImage: $out"
  ls -lh "$out"
}

main() {
  need_cmd dotnet
  need_cmd curl
  ensure_tools
  local icon
  icon="$(prepare_icon)"
  publish_app
  assemble_appdir
  run_linuxdeploy "$icon"
}

main "$@"
