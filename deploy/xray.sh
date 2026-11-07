#!/usr/bin/env bash
# Copies the pinned Xray component into a package.
set -euo pipefail

runtime=${1:?runtime is required}
out=${2:?output directory is required}
version=26.10.31
case $runtime in
  linux-x64|amd64)
    archive=Xray-linux-64.zip
    digest=e4bd9119f50ae7c41658b1abd0679ae06f98bd864a2907ff33f2f2cf7081b214
    ;;
  linux-arm64|arm64)
    archive=Xray-linux-arm64-v8a.zip
    digest=1811c8f1da0c3b6dc707ebaf1c8390e987ba39859a8f0a7aadd8526157f2ec8d
    ;;
  *) echo "no Xray component for $runtime" >&2; exit 1 ;;
esac

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
curl -fsSL --retry 3 --connect-timeout 15 --max-time 300 \
  "https://github.com/XTLS/Xray-core/releases/download/v$version/$archive" -o "$work/$archive"
printf '%s  %s\n' "$digest" "$work/$archive" | sha256sum -c -
mkdir -p "$out"
unzip -p "$work/$archive" xray > "$out/xray"
unzip -p "$work/$archive" LICENSE > "$out/xray.LICENSE"
unzip -p "$work/$archive" README.md > "$out/xray.README.md"
chmod 755 "$out/xray"
