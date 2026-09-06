# The kernel side

The server speaks generic netlink to the `amneziawg` module itself. No `awg` and no `awg-quick` are
installed on the host, and the module is the only source of truth about interfaces and peers.

## What is where

| File | Holds |
|---|---|
| `Netlink/NetlinkSocket.cs` | the socket, the request and the gathering of a multipart answer |
| `Netlink/NetlinkWriter.cs` | the wire layout of a request |
| `Netlink/NetlinkAttributes.cs` | walking and reading the attributes of an answer |
| `Netlink/GenericNetlink.cs` | resolving a family through the control family |
| `Uapi/WgUapi.cs` | the constants, generated from the header of the module |
| `Device/DeviceReader.cs` | a dump turned into one interface |
| `Device/DeviceWriter.cs` | a change turned into requests |
| `Device/AwgDevices.cs` | the interfaces of the host, read and changed |

Constants are generated, not written by hand: after the submodule moves, run
`python3 tools/generate-uapi.py`.

## Reading

`AwgDevices.Names()` walks `/sys/class/net` and keeps what carries `DEVTYPE=amneziawg`.
`AwgDevices.Find(name)` sends `WG_CMD_GET_DEVICE` as a dump and joins the answer: the kernel writes the
head of the interface once and then splits the peers across messages, and a peer with many ranges is
written again under the same public key. `List()` reads every interface of the host.

Both reading and changing need `CAP_NET_ADMIN`; without it the kernel answers `EPERM` and the message
says so.

## Changing

`AwgDevices.Apply(update)` carries only what is set. Nothing is replaced unless it is asked for:
`ReplacePeers` clears the peers of the interface, `ReplaceAllowedIps` clears the ranges of one peer,
`Remove` takes a peer off, `UpdateOnly` drops the change when the peer is not already there. A change too
large for one message is split, and only the first message carries the flags of the interface.

## What the module does its own way

- `h1` to `h4` are spans packed into eight bytes, low half first. So are `content padding addition`,
  the four timers, `max handshake attempts` and the keepalive of a peer, packed into four bytes.
  `AwgRange` holds them; a single number is a span of itself.
- A zero span is what the kernel takes as its own default, so zero headers are not written at all:
  the module refuses four zero spans as overlapping.
- `WGPEER_A_PERSISTENT_KEEPALIVE_INTERVAL` is four bytes here, not the two the header of upstream
  WireGuard describes.
- The dump puts `WGPEER_F_HAS_ADVANCED_SECURITY` on every peer: it tells that the module carries
  advanced security, not that this peer was set up with it.
- `i1` to `i5` are packets written as tags: `<b 0xf1a2>` bytes, `<c>` a counter, `<t>` the time,
  `<r 12>` random bytes, `<rc 12>` random letters, `<rd 12>` random digits. `<c>` and `<t>` take no
  value, and a value on them is refused.
- A header protection key needs `s1` to `s4` of at least 12 bytes, otherwise the change is refused.

## From the console

```
device list
device show [name] [--secrets]
device set <name> [--key <base64>] [--generate] [--port <number>] [--fwmark <number>]
            [--shape jc=4,jmin=40,jmax=70,s1=15,h1=1-3,keepalive=25] [--i1 <spec>] [--hpk <base64>]
device peer <name> <public key> [--allowed <a,b>] [--endpoint <host:port>] [--preshared <base64>]
            [--keepalive <seconds>] [--replace-ips] [--update-only] [--remove]
```

`--generate` writes 32 random bytes as the private key and prints them; the public key is what the
kernel derives, and it comes back with `device show`.
