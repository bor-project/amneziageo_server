#!/usr/bin/env bash
# Puts the panel of this package on the host, whole or its web interface alone, and goes back a release.
set -euo pipefail

base=/opt/amneziageo-server
data=/var/lib/amneziageo-server
unit=amneziageo-server
here=$(cd "$(dirname "$0")" && pwd)
release=$(cat "$here/release" 2>/dev/null || date -u +%Y%m%d-%H%M%S)
force=

fail() {
  echo "$*" >&2
  exit 1
}

usage() {
  cat >&2 <<'EOF'
usage:
  install.sh              put the whole panel on and start the server over on it
  install.sh --ui         put the web interface on alone, the server keeps running
  install.sh --rollback   go back to the release before the current one
  install.sh --list       show the releases the host keeps
  --force                 put the package on where the host already runs it, or runs another server
EOF
  exit 2
}

# Points a link at a target in one step.
point() {
  ln -sfn "$2" "$1.next"
  mv -Tf "$1.next" "$1"
}

# Puts a file in place where it differs, telling whether it did.
put() {
  if [ -f "$3" ] && cmp -s "$2" "$3"; then
    return 1
  fi

  install -m "$1" "$2" "$3"
}

# Moves a panel put on before releases into a release of its own.
adopt() {
  local name
  if [ -L "$base/AmneziaGeo.Server.Api" ] || [ ! -f "$base/AmneziaGeo.Server.Api" ]; then
    return 0
  fi

  name=legacy-$(date -u -r "$base/AmneziaGeo.Server.Api" +%Y%m%d-%H%M%S)
  mkdir -p "$base/releases/$name" "$base/web"
  find "$base" -mindepth 1 -maxdepth 1 ! -name releases ! -name web -exec mv -t "$base/releases/$name" {} +
  if [ -d "$base/releases/$name/wwwroot" ]; then
    mv -T "$base/releases/$name/wwwroot" "$base/web/$name"
  else
    mkdir -p "$base/web/$name"
  fi

  point "$base/current" "releases/$name"
  point "$base/wwwroot" "web/$name"
  echo "the panel put on before releases is kept as $name"
}

# Puts the server of the package into a release of its own, the web interface left out.
stage_release() {
  local target=$base/releases/$release
  if [ -d "$target" ]; then
    return 0
  fi

  rm -rf "$target.new"
  cp -a "$here/publish" "$target.new"
  rm -rf "$target.new/wwwroot"
  cp "$here/install.sh" "$here/release" "$here/server" "$target.new/"
  if [ -f "$here/amneziageo-server" ]; then
    install -m 755 "$here/amneziageo-server" "$target.new/"
  fi

  chmod +x "$target.new/AmneziaGeo.Server.Api" "$target.new/AmneziaGeo.Server.Cli" "$target.new/install.sh"
  mv -T "$target.new" "$target"
}

# Puts a web interface into a directory of its own, with the files pages opened on the one before may still ask for.
stage_web() {
  local target=$base/web/$release before file name
  if [ -d "$target" ]; then
    return 0
  fi

  rm -rf "$target.new"
  cp -a "$1" "$target.new"
  before=$(readlink -f "$base/wwwroot" || true)
  if [ -n "$before" ] && [ -d "$before/assets" ]; then
    mkdir -p "$target.new/assets"
    for file in "$before"/assets/*; do
      name=${file##*/}
      if [ ! -f "$file" ] || [ -e "$target.new/assets/$name" ] || grep -qxF "$name" "$before/.carried" 2>/dev/null; then
        continue
      fi

      cp -p "$file" "$target.new/assets/"
      echo "$name" >> "$target.new/.carried"
    done
  fi

  mv -T "$target.new" "$target"
}

# Puts the services, the websocket tool and the menu in place where they differ, and drops the unit of the relays.
shared() {
  local reload= file
  for file in amneziageo-server.service amneziageo-proxy@.service; do
    if put 644 "$here/$file" "/etc/systemd/system/$file"; then
      reload=1
    fi
  done

  if [ -f /etc/systemd/system/amneziageo-relay@.service ]; then
    rm -f /etc/systemd/system/amneziageo-relay@.service
    reload=1
  fi

  put 755 "$here/wstunnel" /usr/local/bin/wstunnel || true
  if [ -f "$here/amneziageo-server" ]; then
    put 755 "$here/amneziageo-server" /usr/local/bin/amneziageo-server || true
  fi

  if [ -n "$reload" ]; then
    systemctl daemon-reload
  fi

  if [ ! -f /etc/amneziageo-server/server.env ]; then
    cat > /etc/amneziageo-server/server.env <<'ENV'
# Web__Listen__0=*:8443
# Web__Path=/
# Web__Certificate=/etc/letsencrypt/live/example.org/fullchain.pem
# Web__CertificateKey=/etc/letsencrypt/live/example.org/privkey.pem
ENV
    chmod 600 /etc/amneziageo-server/server.env
  fi
}

# Leads the paths the docs name to the current release.
shortcuts() {
  local tool
  for tool in AmneziaGeo.Server.Api AmneziaGeo.Server.Cli; do
    point "$base/$tool" "current/$tool"
  done
}

# Copies the database of the stopped server aside and prints where.
keep_database() {
  local target
  if [ ! -f "$data/server.db" ]; then
    return 0
  fi

  target=$data/backup/$(date -u +%Y%m%d-%H%M%S)-$release
  mkdir -p "$target"
  cp -a "$data"/server.db* "$target/"
  echo "$target"
}

# Puts a copy of the database back.
restore_database() {
  if [ -z "$1" ] || [ ! -d "$1" ]; then
    return 0
  fi

  rm -f "$data/server.db-wal" "$data/server.db-shm"
  cp -a "$1"/server.db* "$data/"
}

# Starts the server and tells whether it came up and stayed up.
start_server() {
  local pid
  systemctl start "$unit" || return 1
  pid=$(systemctl show -p MainPID --value "$unit")
  sleep 5
  [ "$(systemctl is-active "$unit")" = active ] && [ "$(systemctl show -p MainPID --value "$unit")" = "$pid" ]
}

# Drops the releases and web interfaces the host no longer points at, and the oldest copies of the database.
prune() {
  local keep dir
  keep=" $(readlink -f "$base/current") $(readlink -f "$base/previous" || true) "
  for dir in "$base"/releases/*; do
    if [ -d "$dir" ] && [[ $keep != *" $dir "* ]]; then
      rm -rf "$dir"
    fi
  done

  keep=" $(readlink -f "$base/wwwroot") $(readlink -f "$base/previous-web" || true)"
  keep="$keep $base/web/$(basename "$(readlink "$base/current")")"
  keep="$keep $base/web/$(basename "$(readlink "$base/previous" || echo none)") "
  for dir in "$base"/web/*; do
    if [ -d "$dir" ] && [[ $keep != *" $dir "* ]]; then
      rm -rf "$dir"
    fi
  done

  if [ -d "$data/backup" ]; then
    find "$data/backup" -mindepth 1 -maxdepth 1 -type d | sort | head -n -5 | xargs -r rm -rf
  fi
}

# Puts the whole panel on and starts the server over on it, going back where the new release does not come up.
whole() {
  local before before_web before_previous was saved
  if [ ! -d "$here/publish" ]; then
    fail "this package carries no server: put it on with --ui"
  fi

  install -d "$base/releases" "$base/web" "$data" /etc/amneziageo-server
  adopt
  before=$(readlink "$base/current" || true)
  before_web=$(readlink "$base/wwwroot" || true)
  before_previous=$(readlink "$base/previous" || true)
  if [ "$before" = "releases/$release" ] && [ -z "$force" ]; then
    echo "the host already runs $release"
    return 0
  fi

  stage_release
  stage_web "$here/publish/wwwroot"
  shared
  was=$(systemctl is-active "$unit" || true)
  if [ "$was" = active ]; then
    systemctl stop "$unit"
  fi

  saved=$(keep_database)
  point "$base/current" "releases/$release"
  point "$base/wwwroot" "web/$release"
  if [ -n "$before" ] && [ "$before" != "releases/$release" ]; then
    point "$base/previous" "$before"
    point "$base/previous-web" "$before_web"
  fi

  shortcuts
  systemctl enable --quiet "$unit"
  if [ "$was" != active ]; then
    prune
    echo "$release is on the host, the server is not running"
    if [ ! -f "$data/server.db" ]; then
      echo "  $base/AmneziaGeo.Server.Cli init      make the first administrator"
    fi

    echo "  systemctl start $unit                  start it"
    return 0
  fi

  if start_server; then
    prune
    echo "the host runs $release${before:+, install.sh --rollback goes back to ${before##*/}}"
    return 0
  fi

  echo "$release did not come up, the host goes back to ${before##*/}" >&2
  systemctl stop "$unit" || true
  restore_database "$saved"
  point "$base/current" "$before"
  point "$base/wwwroot" "$before_web"
  if [ -n "$before_previous" ]; then
    point "$base/previous" "$before_previous"
  fi

  systemctl start "$unit" || true
  prune
  fail "journalctl -u $unit tells what stopped $release"
}

# Puts the web interface on alone while the server runs on.
web() {
  local source=$here/wwwroot old
  if [ ! -d "$source" ]; then
    source=$here/publish/wwwroot
  fi

  if [ ! -d "$source" ]; then
    fail "this package carries no web interface"
  fi

  if [ ! -L "$base/current" ] || [ ! -L "$base/wwwroot" ]; then
    fail "the host runs a panel put on before releases: put the whole package on first"
  fi

  if [ -z "$force" ] && ! cmp -s "$here/server" "$base/current/server"; then
    fail "this web interface was built for another server: put the whole package on, or add --force"
  fi

  old=$(readlink "$base/wwwroot")
  if [ "$old" = "web/$release" ] && [ -z "$force" ]; then
    echo "the host already serves the web interface $release"
    return 0
  fi

  stage_web "$source"
  if [ "$old" != "web/$release" ]; then
    point "$base/previous-web" "$old"
  fi

  point "$base/wwwroot" "web/$release"
  prune
  echo "the host serves the web interface $release, the server kept running"
}

# Goes back to the release before the current one and starts the server over on it.
rollback() {
  local before after web was
  before=$(readlink "$base/previous" || true)
  after=$(readlink "$base/current" || true)
  if [ -z "$before" ] || [ ! -d "$base/$before" ]; then
    fail "there is no release to go back to"
  fi

  web=web/${before##*/}
  if [ ! -d "$base/$web" ]; then
    web=$(readlink "$base/previous-web" || readlink "$base/wwwroot")
  fi

  was=$(systemctl is-active "$unit" || true)
  if [ "$was" = active ]; then
    systemctl stop "$unit"
  fi

  point "$base/previous-web" "$(readlink "$base/wwwroot")"
  point "$base/wwwroot" "$web"
  point "$base/current" "$before"
  point "$base/previous" "$after"
  if [ "$was" = active ] && ! start_server; then
    fail "the server did not come up on ${before##*/}, journalctl -u $unit tells why"
  fi

  echo "the host runs ${before##*/}, install.sh --rollback goes back to ${after##*/}"
  echo "the database stays as it is, the copies taken before each update lie in $data/backup"
}

# Shows the releases the host keeps.
list() {
  echo "current   $(readlink "$base/current" || echo none)"
  echo "previous  $(readlink "$base/previous" || echo none)"
  echo "wwwroot   $(readlink "$base/wwwroot" || echo none)"
  echo
  ls -1 "$base/releases" "$base/web" 2>/dev/null || true
}

mode=whole
for word in "$@"; do
  case $word in
    --ui) mode=web ;;
    --rollback) mode=rollback ;;
    --list) mode=list ;;
    --force) force=1 ;;
    *) usage ;;
  esac
done

if [ "$(id -u)" != 0 ]; then
  fail "run this as root"
fi

if [ "$mode" = whole ] && [ ! -d "$here/publish" ] && [ -d "$here/wwwroot" ]; then
  mode=web
fi

"$mode"
