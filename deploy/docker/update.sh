#!/bin/bash
# Moves the panel of a compose project onto the image of a release, and back where it does not come up healthy.
set -u

data=$AMNEZIAGEO_UPDATE_DATA
work=$AMNEZIAGEO_UPDATE_WORK
tag=$AMNEZIAGEO_UPDATE_TAG
from=$AMNEZIAGEO_UPDATE_FROM
image=$AMNEZIAGEO_UPDATE_IMAGE
service=$AMNEZIAGEO_UPDATE_SERVICE
mkdir -p "$work"
exec >> "$work/$tag.log" 2>&1

say() {
  echo "$(date -u +%H:%M:%S) $*"
}

finish() {
  echo "$1" > "$work/$tag.rc"
  exit "$1"
}

files=()
IFS=, read -r -a listed <<< "$AMNEZIAGEO_UPDATE_FILES"
for file in "${listed[@]}"; do
  files+=(-f "$file")
done

compose() {
  docker-compose -p "$AMNEZIAGEO_UPDATE_PROJECT" "${files[@]}" "$@"
}

# Prints the health of the service, or its state where the image carries no health check.
health() {
  compose ps --all --format '{{if .Health}}{{.Health}}{{else}}{{.State}}{{end}}' "$service" 2>/dev/null | head -n 1
}

# Puts the database and the tag of the image back and starts the panel on the image it ran before.
back() {
  say "$tag did not come up healthy, the panel goes back to $from"
  compose logs --no-color --tail 40 "$service"
  compose stop "$service"
  if [ -n "$backup" ] && [ -d "$backup" ]; then
    rm -f "$data/server.db-wal" "$data/server.db-shm"
    cp -a "$backup"/server.db* "$data/"
  fi

  if [ -f "$work/env.before" ]; then
    cp -p "$work/env.before" .env
  else
    rm -f .env
  fi

  compose up -d --no-build "$service"
  finish 1
}

cd "$AMNEZIAGEO_UPDATE_DIR" || finish 1
say "moving $service of $AMNEZIAGEO_UPDATE_PROJECT from $from to $image"

named=$(AMNEZIAGEO_TAG=$tag compose config --images "$service" 2>/dev/null | head -n 1)
if [ "$named" != "$image" ]; then
  say "the compose file names ${named:-no image} with AMNEZIAGEO_TAG=$tag, not $image"
  finish 1
fi

rm -f "$work/env.before"
if [ -f .env ]; then
  cp -p .env "$work/env.before"
fi

say "stopping $service"
compose stop "$service" || finish 1

backup=
if [ -f "$data/server.db" ]; then
  backup=$data/backup/$(date -u +%Y%m%d-%H%M%S)-$from
  if ! mkdir -p "$backup" || ! cp -a "$data"/server.db* "$backup/"; then
    say "the database was not copied aside, the panel starts again on $from"
    compose start "$service"
    finish 1
  fi

  say "the database is copied to $backup"
fi

if grep -q '^AMNEZIAGEO_TAG=' .env 2>/dev/null; then
  sed -i "s|^AMNEZIAGEO_TAG=.*|AMNEZIAGEO_TAG=$tag|" .env
else
  echo "AMNEZIAGEO_TAG=$tag" >> .env
fi

say "starting $service on $image"
compose up -d --no-build "$service" || back

state=
for _ in $(seq 1 60); do
  state=$(health)
  if [ "$state" = healthy ]; then
    break
  fi

  sleep 2
done

if [ "$state" != healthy ]; then
  back
fi

say "$service runs $tag"
if [ -d "$data/backup" ]; then
  find "$data/backup" -mindepth 1 -maxdepth 1 -type d | sort | head -n -5 | xargs -r rm -rf
fi

finish 0
