#!/usr/bin/env bash
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  exec sudo bash "$0" "$@"
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends \
  file \
  patchelf \
  pkg-config \
  librsvg2-bin \
  adwaita-icon-theme \
  gsettings-desktop-schemas \
  libglib2.0-0 \
  libgdk-pixbuf-2.0-0 \
  librsvg2-2 \
  libgtk-3-0 \
  libgtk-3-dev \
  libayatana-appindicator3-1 \
  libnotify4 \
  libnotify-dev \
  desktop-file-utils
