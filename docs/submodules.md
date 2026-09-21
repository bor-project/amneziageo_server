# Submodules

Both are forks in `bor-project`. A fork carries no tags, so each is pinned by commit; the table records
which upstream tag that commit is.

| Submodule | Commit | Upstream tag |
|---|---|---|
| `amneziawg/amneziawg-linux-kernel-module` | `4569c4c` | `v3.1.20260906` |
| `wstunnel/wstunnel` | `db10b1a` | `v10.5.5` |

## The kernel module

It serves two purposes. `src/uapi/wireguard.h` is the source the C# constants are generated from, and the
tree builds the module itself for kernels the Amnezia PPA does not cover.

Regenerate the constants after moving the submodule:

```
python3 tools/generate-uapi.py
```

Build and load the module:

```
make -C amneziawg/amneziawg-linux-kernel-module/src -j"$(nproc)"
sudo cp amneziawg/amneziawg-linux-kernel-module/src/amneziawg.ko /lib/modules/"$(uname -r)"/updates/
sudo depmod -a && sudo modprobe amneziawg
```

`insmod` on the built file fails with `Unknown symbol`: the module needs `libcurve25519` and `udp_tunnel`,
which only `modprobe` pulls in.

The `amneziawg-tools` package is not a submodule and is not required. The server speaks netlink itself, and
`ip link add dev <name> type amneziawg` needs only iproute2, which passes the type string straight to the
kernel.

## The websocket proxy

`wstunnel` carries the UDP of an interface inside a websocket, so a network that passes nothing but web
traffic still carries the tunnel. The pinned commit is the build the servers already run.

Build it:

```
cargo build --release --manifest-path wstunnel/wstunnel/Cargo.toml -p wstunnel-cli
```

`deploy/publish.sh` builds it the same way and puts the binary in the package, `deploy/install.sh` puts it
at `/usr/local/bin/wstunnel` with a service of its own, one instance per endpoint that takes the websocket. The
panel writes the arguments and the whitelist of targets, see [services.md](services.md); the tree of the fork
stays as upstream wrote it.
