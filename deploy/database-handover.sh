#!/usr/bin/env bash
# Keeps database snapshots under their release identities.

db_key() {
  printf '%s' "$1" | sha256sum | cut -d ' ' -f 1
}

db_find() {
  local root=$1 identity=$2 binding saved declared canonical
  binding=$root/rollback/$(db_key "$identity")
  [ -f "$binding" ] || return 1
  { IFS= read -r declared; IFS= read -r saved; } < "$binding" || return 1
  [ "$declared" = "$identity" ] || return 1
  [[ $saved == backup/* && $saved != *'..'* ]] || return 1
  canonical=$(readlink -f "$root/$saved") || return 1
  [[ $canonical == "$(readlink -f "$root/backup")/"* ]] || return 1
  [ -f "$canonical/server.db" ] && [ -f "$canonical/build" ] || return 1
  [ "$(cat "$canonical/build")" = "$identity" ] || return 1
  printf '%s\n' "$canonical"
}

db_save() {
  local root=$1 identity=$2 target binding temporary tail
  [ -f "$root/server.db" ] || return 0
  [[ $identity != *$'\n'* && $identity != *$'\r'* ]] || return 1
  install -d -m 700 "$root/backup" "$root/rollback" || return 1
  target=$(mktemp -d "$root/backup/$(date -u +%Y%m%d-%H%M%S)-database.XXXXXX") || return 1
  for tail in '' -wal -shm -journal; do
    if [ -f "$root/server.db$tail" ]; then
      cp -p "$root/server.db$tail" "$target/" || return 1
    fi
  done
  printf '%s\n' "$identity" > "$target/build" || return 1
  binding=$root/rollback/$(db_key "$identity")
  temporary=$(mktemp "$root/rollback/.binding.XXXXXX") || return 1
  printf '%s\n%s\n' "$identity" "backup/${target##*/}" > "$temporary" || return 1
  chmod 600 "$temporary"
  mv -f "$temporary" "$binding" || return 1
  printf '%s\n' "$target"
}

db_restore() {
  local root=$1 saved=$2 stage aside tail failed=0 moved=0
  [ -n "$saved" ] || return 0
  [ -f "$saved/server.db" ] || return 1
  stage=$(mktemp -d "$root/.database-restore.XXXXXX") || return 1
  for tail in '' -wal -shm -journal; do
    if [ -f "$saved/server.db$tail" ]; then
      cp -p "$saved/server.db$tail" "$stage/" || { rm -rf "$stage"; return 1; }
    fi
  done
  install -d -m 700 "$root/backup" || return 1
  aside=$(mktemp -d "$root/backup/$(date -u +%Y%m%d-%H%M%S)-before-restore.XXXXXX") || return 1
  for tail in '' -wal -shm -journal; do
    if [ -f "$root/server.db$tail" ]; then
      mv "$root/server.db$tail" "$aside/" || { failed=1; break; }
    fi
  done
  if [ "$failed" = 0 ]; then
    moved=1
    for tail in '' -wal -shm -journal; do
      if [ -f "$stage/server.db$tail" ]; then
        mv "$stage/server.db$tail" "$root/" || { failed=1; break; }
      fi
    done
  fi
  if [ "$failed" != 0 ]; then
    if [ "$moved" = 1 ]; then
      for tail in '' -wal -shm -journal; do rm -f "$root/server.db$tail"; done
    fi
    for tail in '' -wal -shm -journal; do
      if [ -f "$aside/server.db$tail" ]; then mv -f "$aside/server.db$tail" "$root/" || true; fi
    done
  fi
  rm -rf "$stage"
  [ "$failed" = 0 ]
}

db_prune() {
  local root=$1 kind=$2 identity binding saved dir count=0 keep=' '
  shift 2
  for binding in "$root"/rollback/*; do
    [ -f "$binding" ] || continue
    { IFS= read -r identity; IFS= read -r saved; } < "$binding" || continue
    if [[ $identity == "$kind:"* ]] && [[ " $* " != *" $identity "* ]]; then
      rm -f "$binding"
    elif [ -d "$root/$saved" ]; then
      keep="$keep$root/$saved "
    fi
  done
  while IFS= read -r dir; do
    [[ $keep == *" $dir "* ]] && continue
    count=$((count + 1))
    if [ "$count" -gt 5 ]; then rm -rf "$dir"; fi
  done < <(find "$root/backup" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort -r)
}
