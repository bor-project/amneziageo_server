#!/usr/bin/env bash
# Builds the pinned QUIC runtime.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
pin=$(tr -d '\n\r' < "$root/deploy/quic.commit")
[[ $pin =~ ^[0-9a-f]{40}$ ]] || exit 2
repository=$root/wstunnel/wstunnel
git -C "$repository" cat-file -e "$pin^{commit}" 2>/dev/null || git -C "$repository" fetch --depth=1 origin "$pin"
patch_hash=$(sha256sum "$root/deploy/quic-udp-only.patch" | cut -c1-16)
stage=${QUIC_BUILD_ROOT:-$root/.local/quic}/$pin-$patch_hash
if [ ! -f "$stage/.prepared" ]; then
  [ ! -e "$stage" ] || { echo "unfinished QUIC source stage: $stage" >&2; exit 1; }
  mkdir -p "$stage"
  git -C "$repository" archive "$pin" | tar -x -C "$stage"
  patch --batch -d "$stage" -p1 < "$root/deploy/quic-udp-only.patch"
  printf '%s\n' "$pin" > "$stage/.prepared"
fi
target=${QUIC_TARGET:-x86_64-unknown-linux-musl}
profile=${QUIC_PROFILE:-release}
args=(build --locked --manifest-path "$stage/Cargo.toml" --package wstunnel-cli --no-default-features --features ring,jemalloc)
if [ "$profile" = release ]; then args+=(--release); fi
if [ "$target" != native ]; then args+=(--target "$target"); fi
env PATH="$HOME/.cargo/bin:$PATH" cargo "${args[@]}"
directory=$stage/target
if [ "$target" != native ]; then directory=$directory/$target; fi
if [ "$profile" = release ]; then directory=$directory/release; else directory=$directory/debug; fi
install -m 755 "$directory/wstunnel" "${1:?output path required}"
printf '%s %s\n' "$pin" "$(sha256sum "${1}")"
