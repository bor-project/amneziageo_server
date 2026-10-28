#!/usr/bin/env bash
# Puts the client updates server of this package on the host, goes back a release and lists the releases it keeps.
set -euo pipefail

base=/opt/amneziageo-updates
data=/var/lib/amneziageo-updates
etc=/etc/amneziageo-updates
unit=amneziageo-updates
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
  install.sh              put the client updates server on, starting it over where it runs
  install.sh --rollback   go back to the release before the current one
  install.sh --list       show the releases the host keeps
  --force                 put the package on where the host already runs it
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

# Puts the program of the package into a release of its own.
stage_release() {
  local target=$base/releases/$release
  if [ -d "$target" ]; then
    return 0
  fi

  rm -rf "$target.new"
  cp -a --no-preserve=ownership "$here" "$target.new"
  chmod +x "$target.new/AmneziaGeo.Server.Updates.Host" "$target.new/install.sh"
  mv -T "$target.new" "$target"
}

# Puts the unit of the service and the menu in place where they differ.
shared() {
  if put 644 "$here/$unit.service" "/etc/systemd/system/$unit.service"; then
    systemctl daemon-reload
  fi

  if [ -f "$here/amneziageo-server" ]; then
    put 755 "$here/amneziageo-server" /usr/local/bin/amneziageo-server || true
  fi
}

# Starts the service and tells whether it came up and stayed up.
start_service() {
  local pid
  systemctl start "$unit" || return 1
  pid=$(systemctl show -p MainPID --value "$unit")
  sleep 5
  [ "$(systemctl is-active "$unit")" = active ] && [ "$(systemctl show -p MainPID --value "$unit")" = "$pid" ]
}

# Drops the releases the host no longer points at.
prune() {
  local keep dir
  keep=" $(readlink -f "$base/current") $(readlink -f "$base/previous" || true) "
  for dir in "$base"/releases/*; do
    if [ -d "$dir" ] && [[ $keep != *" $dir "* ]]; then
      rm -rf "$dir"
    fi
  done
}

# Puts the package on and starts the service over on it where it ran, going back where the new release does not come up.
whole() {
  local before before_previous was
  install -d "$base/releases" "$data"
  install -d -m 700 "$etc"
  before=$(readlink "$base/current" || true)
  before_previous=$(readlink "$base/previous" || true)
  if [ "$before" = "releases/$release" ] && [ -z "$force" ]; then
    echo "the host already runs $release"
    return 0
  fi

  stage_release
  shared
  was=$(systemctl is-active "$unit" || true)
  if [ "$was" = active ]; then
    systemctl stop "$unit"
  fi

  point "$base/current" "releases/$release"
  if [ -n "$before" ] && [ "$before" != "releases/$release" ]; then
    point "$base/previous" "$before"
  fi

  systemctl enable --quiet "$unit"
  if [ "$was" != active ]; then
    prune
    echo "$release is on the host, the service is not running"
    return 0
  fi

  if start_service; then
    prune
    echo "the host runs $release${before:+, install.sh --rollback goes back to ${before##*/}}"
    return 0
  fi

  echo "$release did not come up, the host goes back to ${before##*/}" >&2
  systemctl stop "$unit" || true
  point "$base/current" "$before"
  if [ -n "$before_previous" ]; then
    point "$base/previous" "$before_previous"
  fi

  systemctl start "$unit" || true
  prune
  fail "journalctl -u $unit tells what stopped $release"
}

# Goes back to the release before the current one and starts the service over on it.
rollback() {
  local before after was
  before=$(readlink "$base/previous" || true)
  after=$(readlink "$base/current" || true)
  if [ -z "$before" ] || [ ! -d "$base/$before" ]; then
    fail "there is no release to go back to"
  fi

  was=$(systemctl is-active "$unit" || true)
  if [ "$was" = active ]; then
    systemctl stop "$unit"
  fi

  point "$base/current" "$before"
  point "$base/previous" "$after"
  if [ "$was" = active ] && ! start_service; then
    fail "the service did not come up on ${before##*/}, journalctl -u $unit tells why"
  fi

  echo "the host runs ${before##*/}, install.sh --rollback goes back to ${after##*/}"
}

# Shows the releases the host keeps.
list() {
  echo "current   $(readlink "$base/current" || echo none)"
  echo "previous  $(readlink "$base/previous" || echo none)"
  echo
  ls -1 "$base/releases" 2>/dev/null || true
}

mode=whole
for word in "$@"; do
  case $word in
    --rollback) mode=rollback ;;
    --list) mode=list ;;
    --force) force=1 ;;
    *) usage ;;
  esac
done

if [ "$(id -u)" != 0 ]; then
  fail "run this as root"
fi

"$mode"
