#!/usr/bin/env bash
# Builds the proxy runtimes.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
runtime=${1:?runtime required}
out=${2:?output required}
  case $runtime in
    linux-x64) target=x86_64-unknown-linux-musl page=12 atomics= ;;
    linux-arm64) target=aarch64-unknown-linux-musl page=14 atomics=-mno-outline-atomics ;;
    *)
      echo "no websocket tool for $runtime" >&2
      exit 1
      ;;
  esac
  compiler=CC_${target//-/_}
  flags=CFLAGS_${target//-/_}
  env PATH="$HOME/.cargo/bin:$PATH" "$compiler=${!compiler:-musl-gcc}" "$flags=${!flags:-} $atomics" \
    JEMALLOC_SYS_WITH_LG_PAGE=$page \
    cargo build --locked --release \
    --manifest-path "$root/wstunnel/wstunnel/Cargo.toml" \
    --package wstunnel-cli \
    --no-default-features --features ring,jemalloc \
    --target "$target"
  cp "$root/wstunnel/wstunnel/target/$target/release/wstunnel" "$out/"
  if readelf -lW "$out/wstunnel" | grep -q 'program interpreter'; then
    echo "wstunnel is linked dynamically" >&2
    exit 1
  fi

  "$out/wstunnel" get-server --help >/dev/null
  cp "$out/wstunnel" "$out/wstunnel-get"

  env PATH="$HOME/.cargo/bin:$PATH" "$compiler=${!compiler:-musl-gcc}" "$flags=${!flags:-} $atomics" \
    JEMALLOC_SYS_WITH_LG_PAGE=$page QUIC_TARGET=$target \
    bash "$root/deploy/build-quic.sh" "$out/wstunnel-quic"
