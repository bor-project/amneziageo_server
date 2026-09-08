#!/usr/bin/env bash
# Builds the panel into one directory a server takes as it is.
set -e

root=$(cd "$(dirname "$0")/.." && pwd)
out=${1:-$root/out/amneziageo-server}

rm -rf "$out"
mkdir -p "$out"

npm --prefix "$root/amneziageo-web" run build

for project in Api Cli; do
  dotnet publish "$root/amneziageo-server/AmneziaGeo.Server.$project" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --nologo \
    --output "$out/publish"
done

cp "$root/deploy/amneziageo-server.service" "$out/"
cp "$root/deploy/install.sh" "$out/"
chmod +x "$out/install.sh"

tar -czf "$out.tar.gz" -C "$(dirname "$out")" "$(basename "$out")"
du -sh "$out.tar.gz"
