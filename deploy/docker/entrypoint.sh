#!/bin/sh
# Starts the panel in the network of the host, names what the host still lacks, and stops, holds or starts the panel
# over when the menu of the container asks.
set -eu

run=/run/amneziageo-server
panel=

say() {
  echo "amneziageo-server: $*" >&2
}

# Stops the panel along with the container and leaves with its exit code.
leave() {
  code=0
  if [ -n "$panel" ]; then
    kill -TERM "$panel" 2>/dev/null || true
    wait "$panel" || code=$?
  fi

  exit "$code"
}

mkdir -p "$(dirname "$AMNEZIAGEO_DB")" "$(dirname "$AMNEZIAGEO_SIGNING_KEY")" "$Endpoints__Directory"
rm -rf "$run"
mkdir -p "$run"

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

trap leave TERM INT
while :; do
  if [ -e "$run/held" ]; then
    sleep 1 &
    wait $! || true
    continue
  fi

  /opt/amneziageo-server/AmneziaGeo.Server.Api "$@" &
  panel=$!
  echo "$panel" > "$run/panel.pid"
  code=0
  wait "$panel" || code=$?
  panel=
  rm -f "$run/panel.pid"
  if [ -e "$run/again" ]; then
    rm -f "$run/again"
  elif [ ! -e "$run/held" ]; then
    exit "$code"
  fi
done
