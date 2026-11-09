#!/usr/bin/env bash
# Installs the independent proxy service.
set -euo pipefail
base=/opt/amneziageo-proxy
config=/etc/amneziageo-proxy
data=/var/lib/amneziageo-proxy
unit=amneziageo-proxy.service
here=$(cd "$(dirname "$0")" && pwd)
action=${1:-install}
release=$(cat "$here/release" 2>/dev/null || date -u +%Y%m%d-%H%M%S)
[[ $release =~ ^[a-zA-Z0-9._-]+$ ]] || { echo 'invalid release' >&2; exit 1; }
[ "$(id -u)" = 0 ] || { echo 'run as root' >&2; exit 1; }
point() {
  ln -sfn "$2" "$1.next"
  mv -Tf "$1.next" "$1"
}
case $action in
  --disable)
    systemctl disable --now "$unit"
    exit 0
    ;;
  --remove)
    systemctl disable --now "$unit" || true
    rm -f /etc/systemd/system/amneziageo-proxy.service /usr/local/bin/amneziageo-proxy
    systemctl daemon-reload
    echo "proxy service removed; configurations and releases kept in $config and $base"
    exit 0
    ;;
  --rollback)
    [ -L "$base/previous" ] || { echo 'no previous proxy release' >&2; exit 1; }
    before=$(readlink "$base/current")
    previous=$(readlink "$base/previous")
    point "$base/current" "$previous"
    if systemctl restart "$unit" && systemctl is-active --quiet "$unit"; then
      point "$base/previous" "$before"
      exit 0
    fi
    point "$base/current" "$before"
    systemctl restart "$unit" || true
    exit 1
    ;;
  install) ;;
  *) echo 'usage: proxy-install.sh [--disable|--remove|--rollback]' >&2; exit 2 ;;
esac
source=$here/publish
[ -x "$source/AmneziaGeo.Server.Proxy.Host" ] || source=$here
[ -x "$source/AmneziaGeo.Server.Proxy.Host" ] || { echo 'proxy executable missing' >&2; exit 1; }
for tool in wstunnel wstunnel-get wstunnel-quic; do
  [ -x "$here/$tool" ] || { echo "$tool missing" >&2; exit 1; }
done
install -d -m 755 "$base/releases"
install -d -m 700 "$config" "$data"
target=$base/releases/$release
if [ ! -d "$target" ]; then
  [ ! -e "$target.new" ] || { echo "unfinished proxy release: $target.new" >&2; exit 1; }
  cp -a --no-preserve=ownership "$source" "$target.new"
  for tool in wstunnel wstunnel-get wstunnel-quic; do install -m 755 "$here/$tool" "$target.new/$tool"; done
  install -m 755 "$here/proxy-install.sh" "$target.new/proxy-install.sh"
  cp "$here/release" "$target.new/release"
  chown -R root:root "$target.new"
  mv -T "$target.new" "$target"
fi
before=$(readlink "$base/current" || true)
point "$base/current" "releases/$release"
if [ ! -f "$config/proxies.json" ]; then
  "$base/current/AmneziaGeo.Server.Proxy.Host" init "$config/proxies.json"
fi
if [ ! -f "$config/service.env" ]; then
  install -m 600 /dev/null "$config/service.env"
fi
install -m 644 "$here/amneziageo-proxy.service" /etc/systemd/system/amneziageo-proxy.service
ln -sfn "$base/current/AmneziaGeo.Server.Proxy.Host" /usr/local/bin/amneziageo-proxy
systemctl daemon-reload
systemctl enable "$unit"
if systemctl restart "$unit" && systemctl is-active --quiet "$unit"; then
  if [ -n "$before" ] && [ "$before" != "releases/$release" ]; then point "$base/previous" "$before"; fi
  echo "proxy service installed: $release"
  exit 0
fi
if [ -n "$before" ]; then
  point "$base/current" "$before"
  systemctl restart "$unit" || true
fi
exit 1
