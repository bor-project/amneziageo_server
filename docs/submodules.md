# Submodule

`amneziawg/amneziawg-linux-kernel-module` is a fork in `bor-project`. The fork carries no tags, so it is
pinned by commit; the table records which upstream tag that commit is.

| Submodule | Commit | Upstream tag |
|---|---|---|
| `amneziawg-linux-kernel-module` | `4569c4c` | `v3.1.20260906` |

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
