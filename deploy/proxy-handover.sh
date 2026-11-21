#!/usr/bin/env bash
# Transfers proxy service ownership between releases.

proxy_units() {
  {
    systemctl list-units --all --plain --no-legend || return 1
    systemctl list-unit-files --no-legend || return 1
  } | awk '$1 ~ /^amneziageo-proxy@[^/[:space:]]+\.service$/ {print $1}' | LC_ALL=C sort -u
}

proxy_snapshot() {
  local target=$1 name enabled active names
  install -d -m 700 "$target" || return 1
  awk '/^Proxy__(Enabled|Isolated)=/' /etc/amneziageo-server/server.env > "$target/panel.env" || return 1
  : > "$target/legacy" || return 1
  names=$(proxy_units) || { echo 'could not enumerate legacy proxy services' >&2; return 1; }
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    enabled=$(systemctl is-enabled "$name" 2>/dev/null || true)
    active=$(systemctl is-active "$name" 2>/dev/null || true)
    printf '%s\t%s\t%s\n' "$name" "$enabled" "$active" >> "$target/legacy" || return 1
  done <<< "$names"
  proxy_snapshot_daemon "$target" || return 1
  proxy_snapshot_files "$target"
}

proxy_snapshot_files() {
  local target=$1 file staging
  staging=$(mktemp -d "$target/fronts.XXXXXX") || return 1
  for file in /etc/amneziageo-server/proxy-*.env /etc/amneziageo-server/proxy-*.yaml; do
    [ -e "$file" ] || [ -L "$file" ] || continue
    cp -a -- "$file" "$staging/" || { rm -rf -- "$staging"; return 1; }
  done
  rm -rf -- "$target/fronts" || return 1
  mv -T -- "$staging" "$target/fronts"
}

proxy_restore_files() {
  local snapshot=$1 file name staging
  [ -d "$snapshot/fronts" ] || return 0
  staging=$(mktemp -d /etc/amneziageo-server/proxy-restore.XXXXXX) || return 1
  cp -a -- "$snapshot/fronts/." "$staging/" || { rm -rf -- "$staging"; return 1; }
  for file in /etc/amneziageo-server/proxy-*.env /etc/amneziageo-server/proxy-*.yaml; do
    [ -e "$file" ] || [ -L "$file" ] || continue
    name=${file##*/}
    if [ ! -e "$staging/$name" ] && [ ! -L "$staging/$name" ]; then
      rm -f -- "$file" || { rm -rf -- "$staging"; return 1; }
    fi
  done
  for file in "$staging"/*; do
    [ -e "$file" ] || [ -L "$file" ] || continue
    name=${file##*/}
    mv -fT -- "$file" "/etc/amneziageo-server/$name" || { rm -rf -- "$staging"; return 1; }
  done
  rmdir -- "$staging"
}

proxy_check_restore_files() {
  local snapshot=$1 name enabled active instance
  [ ! -d "$snapshot/fronts" ] || return 0
  while IFS=$'\t' read -r name enabled active; do
    case $active in
      active|activating|reloading)
        instance=${name#amneziageo-proxy@}
        instance=${instance%.service}
        if [ ! -f "/etc/amneziageo-server/proxy-$instance.env" ]; then
          echo "legacy proxy configuration backup is missing for $name; restore the pre-upgrade configuration before rollback" >&2
          return 1
        fi
        ;;
    esac
  done < "$snapshot/legacy"
}

proxy_snapshot_daemon() {
  local target=$1 file
  install -d -m 700 "$target" || return 1
  systemctl is-enabled amneziageo-proxy.service > "$target/daemon-enabled" 2>/dev/null || true
  systemctl is-active amneziageo-proxy.service > "$target/daemon-active" 2>/dev/null || true
  for file in current previous; do
    readlink "/opt/amneziageo-proxy/$file" > "$target/daemon-$file" || true
  done
  for file in /etc/systemd/system/amneziageo-proxy.service /usr/local/bin/amneziageo-proxy; do
    if [ -e "$file" ] || [ -L "$file" ]; then
      cp -a "$file" "$target/$(basename "$file")" || return 1
    fi
  done
  file=/run/systemd/system/amneziageo-proxy.service
  if [ -e "$file" ] || [ -L "$file" ]; then
    cp -a "$file" "$target/daemon-unit-runtime" || return 1
  fi
}

proxy_enabled() {
  local name=$1 state=$2
  case $state in
    masked) systemctl mask "$name" ;;
    masked-runtime) systemctl mask --runtime "$name" ;;
    enabled) systemctl unmask "$name"; systemctl enable "$name" ;;
    enabled-runtime) systemctl unmask "$name"; systemctl enable --runtime "$name" ;;
    disabled) systemctl unmask "$name"; systemctl disable "$name" ;;
    static|indirect|generated|transient|alias|linked|linked-runtime|not-found|'') ;;
    *) echo "unsupported proxy enable state for $name: $state" >&2; return 1 ;;
  esac
}

proxy_start_saved() {
  case $2 in
    active|activating|reloading) systemctl start "$1" ;;
  esac
}

proxy_suspend_legacy() {
  local snapshot=$1 name enabled active
  while IFS=$'\t' read -r name enabled active; do
    [ -n "$name" ] || continue
    systemctl stop "$name" || return 1
    case $enabled in
      enabled|enabled-runtime|disabled) systemctl disable "$name" || return 1; systemctl disable --runtime "$name" || return 1 ;;
      masked|masked-runtime|static|indirect|generated|transient|alias|linked|linked-runtime) ;;
      *) echo "unsupported proxy enable state for $name: $enabled" >&2; return 1 ;;
    esac
  done < "$snapshot/legacy"
}

proxy_restore_daemon() {
  local snapshot=$1 file name target enabled active failed=0
  if ! systemctl stop amneziageo-proxy.service; then
    [ "$(systemctl show -p LoadState --value amneziageo-proxy.service)" = not-found ] || return 1
  fi
  for name in current previous; do
    target=$(cat "$snapshot/daemon-$name")
    if [ -n "$target" ]; then
      point "/opt/amneziageo-proxy/$name" "$target" || failed=1
    else
      rm -f "/opt/amneziageo-proxy/$name" || failed=1
    fi
  done
  systemctl disable amneziageo-proxy.service 2>/dev/null || true
  systemctl disable --runtime amneziageo-proxy.service 2>/dev/null || true
  for file in /etc/systemd/system/amneziageo-proxy.service /usr/local/bin/amneziageo-proxy; do
    rm -f "$file" || failed=1
    if [ -e "$snapshot/$(basename "$file")" ] || [ -L "$snapshot/$(basename "$file")" ]; then
      cp -a "$snapshot/$(basename "$file")" "$file" || failed=1
    fi
  done
  file=/run/systemd/system/amneziageo-proxy.service
  rm -f "$file" || failed=1
  if [ -e "$snapshot/daemon-unit-runtime" ] || [ -L "$snapshot/daemon-unit-runtime" ]; then
    cp -a "$snapshot/daemon-unit-runtime" "$file" || failed=1
  fi
  systemctl daemon-reload || failed=1
  enabled=$(cat "$snapshot/daemon-enabled")
  active=$(cat "$snapshot/daemon-active")
  proxy_enabled amneziageo-proxy.service "$enabled" || failed=1
  proxy_start_saved amneziageo-proxy.service "$active" || failed=1
  [ "$failed" = 0 ]
}

proxy_restore() {
  local snapshot=$1 name enabled active failed=0
  proxy_restore_daemon "$snapshot" || return 1
  while IFS=$'\t' read -r name enabled active; do
    [ -n "$name" ] || continue
    systemctl stop "$name" || return 1
  done < "$snapshot/legacy"
  proxy_restore_files "$snapshot" || { echo 'could not restore legacy proxy configuration files' >&2; return 1; }
  sed '/^Proxy__Enabled=/d; /^Proxy__Isolated=/d' /etc/amneziageo-server/server.env > /etc/amneziageo-server/server.env.next || failed=1
  cat "$snapshot/panel.env" >> /etc/amneziageo-server/server.env.next || failed=1
  chmod 600 /etc/amneziageo-server/server.env.next || failed=1
  mv -f /etc/amneziageo-server/server.env.next /etc/amneziageo-server/server.env || failed=1
  while IFS=$'\t' read -r name enabled active; do
    [ -n "$name" ] || continue
    proxy_enabled "$name" "$enabled" || failed=1
    proxy_start_saved "$name" "$active" || failed=1
  done < "$snapshot/legacy"
  [ "$failed" = 0 ]
}

proxy_remember() {
  local name=${1##*/} snapshot=$2 root=$data/proxy-handover/releases
  [ -n "$name" ] || return 0
  [[ $name =~ ^[a-zA-Z0-9._-]+$ ]] || return 1
  install -d -m 700 "$root"
  if [ -d "$root/$name" ]; then
    rm -rf "$root/$name"
  fi
  cp -a "$snapshot" "$root/$name"
}

proxy_profile() {
  local name=${1##*/}
  [[ $name =~ ^[a-zA-Z0-9._-]+$ ]] || return 1
  printf '%s/proxy-handover/releases/%s\n' "$data" "$name"
}

proxy_keep_installer() {
  local target=$data/proxy-handover/installer file
  install -d -m 700 "$target"
  for file in install.sh proxy-handover.sh release; do
    if ! cmp -s "$here/$file" "$target/$file"; then
      install -m 700 "$here/$file" "$target/$file"
    fi
  done
}
