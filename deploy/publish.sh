#!/usr/bin/env bash
# Builds the panel into a package a server takes as it is, whole or its web interface alone.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
kind=whole
if [ "${1:-}" = --ui ]; then
  kind=ui
  shift
fi

if [ "$kind" = ui ]; then
  out=${1:-$root/out/amneziageo-web}
else
  out=${1:-$root/out/amneziageo-server}
fi

commit=$(git -C "$root" rev-parse --short HEAD 2>/dev/null || echo local)
if [ -n "$(git -C "$root" status --porcelain 2>/dev/null || true)" ]; then
  commit=$commit-dirty
fi

# Fingerprints the sources of the server, so a web interface knows the server it was built for.
fingerprint() {
  (
    cd "$root"
    if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
      git ls-files -co --exclude-standard -- amneziageo-server
    else
      find amneziageo-server -type f -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/wwwroot/*' -not -name '*.db*'
    fi | LC_ALL=C sort | while IFS= read -r file; do
      if [ -f "$file" ]; then
        sha1sum "$file"
      fi
    done | sha1sum | cut -c1-16
  )
}

rm -rf "$out"
mkdir -p "$out"
date -u +%Y%m%d-%H%M%S-"$commit" > "$out/release"
fingerprint > "$out/server"

npm --prefix "$root/amneziageo-web" run build
cp "$root/deploy/install.sh" "$out/"
chmod +x "$out/install.sh"

if [ "$kind" = ui ]; then
  cp -a "$root/amneziageo-server/AmneziaGeo.Server.Api/wwwroot" "$out/wwwroot"
else
  for project in Api Cli; do
    dotnet publish "$root/amneziageo-server/AmneziaGeo.Server.$project" \
      --configuration Release \
      --runtime linux-x64 \
      --self-contained true \
      --nologo \
      --output "$out/publish"
  done

  PATH="$HOME/.cargo/bin:$PATH" cargo build --release \
    --manifest-path "$root/wstunnel/wstunnel/Cargo.toml" \
    --package wstunnel-cli
  cp "$root/wstunnel/wstunnel/target/release/wstunnel" "$out/"

  cp "$root/deploy/amneziageo-server.service" "$out/"
  cp "$root/deploy/amneziageo-proxy@.service" "$out/"
  cp "$root/deploy/amneziageo-relay@.service" "$out/"
fi

tar -czf "$out.tar.gz" -C "$(dirname "$out")" "$(basename "$out")"
echo "$(cat "$out/release") $(du -sh "$out.tar.gz" | cut -f1) $out.tar.gz"
