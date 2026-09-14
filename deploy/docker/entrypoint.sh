#!/bin/sh
# Starts the panel in the network of the host and names what the host still lacks.
set -eu

say() {
  echo "amneziageo-server: $*" >&2
}

mkdir -p "$(dirname "$AMNEZIAGEO_DB")" "$(dirname "$AMNEZIAGEO_SIGNING_KEY")" "$Endpoints__Directory"

if [ ! -d /sys/module/amneziawg ]; then
  say "the host carries no amneziawg module, the interfaces of the endpoints do not come up"
fi

for knob in /proc/sys/net/ipv4/ip_forward /proc/sys/net/ipv6/conf/all/forwarding; do
  if [ "$(cat "$knob" 2>/dev/null)" != 1 ]; then
    say "forwarding is off in $knob, the interfaces of the endpoints do not come up: turn it on in /etc/sysctl.d of the host"
  fi
done

if nft list chain ip filter FORWARD 2>/dev/null | grep -q 'policy drop'; then
  say "the host drops forwarded packets, clients reach nothing past it: set \"ip-forward-no-drop\": true in /etc/docker/daemon.json"
fi

if [ ! -f "$AMNEZIAGEO_DB" ]; then
  say "no administrator yet: docker compose exec panel amneziageo-server init --user <name>"
fi

exec /opt/amneziageo-server/AmneziaGeo.Server.Api "$@"
