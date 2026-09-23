# The configurations of the server

`Configurations` holds the server endpoints of AmneziaWG: the port clients come to, the keys, the
addresses the interface carries and the whole obfuscation of the 3.1 profile. The panel keeps the
intent in SQLite; the kernel is the fact, and the two are brought together separately.

An endpoint holds every setting of its own: the name, the address clients reach it at, the port, the range it
carries, the name servers and the ranges its clients route into the tunnel, the packet size, the keepalive, the
silence after which a device counts as gone, the ranges kept away from clients, what its clients take from the
tunnel, the masquerade, the open port, the keys and the whole obfuscation. A client template changes what the
file of a client takes, see [templates.md](templates.md).

## Rights

| Route | Right |
|---|---|
| `GET /api/configs` | `state:read` |
| `GET /api/configs/{id}` | `state:read` |
| `GET /api/configs/draft?name=` | `interfaces:write` |
| `POST /api/configs/keys` | `interfaces:write` |
| `POST /api/configs/preshared` | `interfaces:write` |
| `POST /api/configs/import` | `interfaces:write` |
| `POST /api/configs` | `interfaces:write` |
| `PUT /api/configs/{id}` | `interfaces:write` |
| `POST /api/configs/apply` | `interfaces:write` |
| `POST /api/configs/{id}/apply` | `interfaces:write` |
| `DELETE /api/configs/{id}` | `interfaces:write` |

Reading gives the public key to everyone who may read the state. The private key and the preshared key
go only to a caller that holds `interfaces:write`; to anyone else they come back as null.

## The settings

| Setting | Holds |
|---|---|
| Name | the name of the interface, up to 15 characters |
| Host | the address or the host name clients come to, empty when it is not known yet |
| Port | the UDP port the endpoint listens on |
| Services port | the TCP port hello, the measurement and the websocket answer on, empty for the number of the UDP port, see [services.md](services.md) |
| WebSocket proxy | whether the endpoint takes the tunnel inside a websocket on the port of its services; when it does not start, the panel turns it off and says why |
| MTU | the packet size clients take, 0 leaves it to the system |
| Interface address | the address ranges the interface carries |
| Closed to clients | the ranges clients of the endpoint are not let into |
| Raise the interface | whether the panel puts the endpoint on the host |
| NAT for clients | whether what clients send out is masqueraded behind the address of the host |
| Open the port in the firewall | whether the panel holds the port of the endpoint open in the firewall of the host, together with both ways through its interface; set it with `amneziageo-server-cli endpoint open\|close`, see [firewall.md](firewall.md) |
| Access to the clients | what reaches the clients of the endpoint from the tunnel unless a client names it itself: closed, the server alone, or the whole tunnel network, see [clients.md](clients.md) |
| AllowedIPs | the ranges a client sends through the tunnel |
| DNS | the name servers a client takes, unless the resolver of the panel answers inside the tunnel |
| Keepalive | how often a client sends an empty packet, in seconds |
| Client online for | how many seconds after its last packet, the keepalive included, a client counts as online: 60 by default, 10 to 3600 and not under two keepalive intervals; the online state in the list of clients and the guard go by it, see [devices.md](devices.md) |
| Private key | the key of the interface, the public one is counted from it |
| Preshared key | the key added to the handshake, empty when the endpoint carries none |
| Jc, Jmin, Jmax | the junk packets before a handshake and their sizes |
| S1 to S4 | the junk prepended to the four packet kinds |
| H1 to H4 | the type of the four packet kinds, one number or a span like `194488238-194553774` |
| I1 to I5 | the special packets the interface sends |
| Header protection key | the key the packet header is hidden with, 32 bytes in base64 |
| Content padding | the padding added to the content of a transport packet, in bytes |
| Rekey after, Rekey timeout, Reject after, Keepalive timeout, Handshake attempts | the timings of the session, each one number or a span |
| Random trailers, Disable cookies | the two switches of the 3.1 profile |

Every span takes one number or two separated by a dash; the kernel picks a value out of it. An empty
setting leaves the kernel its own default, and the client takes the same lines the interface carries.

## A fresh configuration

`GET /api/configs/draft` answers with settings that already hold together: port 51820 or the first one
above it that nothing listens on, `10.8.0.1/24`, MTU 1420, keepalive 25, DNS `1.1.1.1` and `1.0.0.1`,
AllowedIPs `0.0.0.0/0` and `::/0`, a fresh pair of keys and an obfuscation drawn at random: Jc 3 to 10,
Jmin 50, Jmax 1000, S1 and S2 from 15 to 149, H1 to H4 from 5 up, all four different. The panel offers
the first free name of the `awgN` shape and the TCP port the endpoints already serve on, 0 when there are
none, see [services.md](services.md).

The draft is never written down; it reaches the database only when the endpoint is added.

## Taking an interface file

`POST /api/configs/import` takes `{ name, text }`, the interface file of a host, and answers with an endpoint that
is not saved yet: the private key, the addresses, the port, the packet size and the obfuscation come from the file,
the rest from the defaults. The form of a new interface reads it under `Interface file`, and saving it is the
ordinary `POST /api/configs`, so the checks and the raise on the host are the same; a script sends the answer on to
`POST /api/configs` as it is. The peers of the same file become clients through `POST /api/clients/import`, see
[clients.md](clients.md).

## What is refused

A refusal comes back as `{ error, message }`. The code names the setting behind it, and the panel turns
the code into a phrase of its own language.

| Code | Behind it |
|---|---|
| `bad-interface-name` | the name is empty, longer than 15 characters or outside `^[a-z][a-z0-9_-]*$` |
| `bad-host` | the host is neither an address nor a host name, or longer than 255 characters |
| `bad-port` | the port is outside 1 to 65535 |
| `bad-address` | the interface carries no range, or one of them is not a range |
| `bad-allowed` | the client is given no range, or one of them is not a range |
| `bad-dns` | a name server is not an address |
| `bad-mtu` | the packet size is neither 0 nor between 576 and 9000 |
| `bad-keepalive` | the keepalive is outside 0 to 65535 |
| `bad-key` | the private key is not 32 bytes in base64 |
| `bad-preshared` | the preshared key is not 32 bytes in base64 |
| `bad-junk` | a junk size is outside 0 to 1280, the shortest is above the longest, or S1 plus 56 equals S2 |
| `bad-type` | a packet type is below 5, is neither a number nor a span, or two of the four overlap |
| `bad-special` | a special packet is longer than 1024 characters |
| `bad-span` | a timing is neither a number nor a span of two |
| `bad-blocked` | a range closed to clients is not an address range |
| `bad-header-key` | the header protection key is not 32 bytes in base64 |
| `name-taken` | the panel already carries an endpoint under this name |
| `port-taken` | the panel already listens on this port |
| `bad-services-port` | the port of the services is outside 1 to 65535 |
| `panel-path-needed` | the panel answers on this TCP port from the root, see [serving.md](serving.md) |
| `unknown-config` | there is no endpoint under this number |
| `bad-import` | the interface file carries no private key, or nothing at all |

`S1 + 56 == S2` is refused because it makes an initiation and a response the same size. Nothing keeps
two endpoints from carrying the same address range: they meet only on the host, and the ranges of one
are its own business.

## What the host takes

An endpoint that is turned on is put on the host whole: the interface is added when the host carries none,
the private key, the port and the obfuscation go into the kernel, the address ranges are laid on the
interface and it is brought up at the packet size the endpoint names. Turning an endpoint off takes its
interface off the host, and so does removing it.

The rules travel in the `inet amneziageo_in` table, rewritten whenever an endpoint changes:

| Rule | What it does |
|---|---|
| `iifname "<uplink>" <tcp\|udp> dport <port> dnat to <client>` | carries a port of the host to a client |
| `ip saddr <range> oifname "<uplink>" masquerade` | sends clients out behind the address of the host |
| `ct state established,related accept` | lets the answers to what a client sent back in |
| `oifname "<endpoint>" ip daddr <client>  <tcp\|udp> dport <port> accept` | lets a carried port through |
| `oifname "<endpoint>" ip daddr @in<id>v4 accept` | lets the tunnel reach the clients that take it |
| `oifname "<endpoint>" drop` | holds everything else off the clients of the endpoint |
| `iifname "<endpoint>" ip daddr <closed range> reject` | keeps clients out of the ranges the endpoint closes |

The sets `in<id>v4` and `in<id>v6` carry the addresses of the clients whose access is the whole tunnel
network, together with the networks behind them, see [clients.md](clients.md). A client that leaves the
choice to the endpoint takes what the endpoint says; a client that takes nothing from the tunnel is in no
set, so `drop` holds the other clients off it.

Both families travel the same way, and the host is told to pass packets between interfaces in both of them:
`net.ipv4.ip_forward` and `net.ipv6.conf.all.forwarding` are set on every raise, so a host that was rebooted
carries them again as soon as the server starts.
`POST /api/configs/{id}/apply` puts one endpoint on the host again, `POST /api/configs/apply` all of them.
Every endpoint is raised once more when the server starts, before the clients are laid on top. An interface
that is already up keeps its peers and the addresses it carries: only the addresses that differ are changed.

## The keys

The pair is made by `AmneziaGeo.Server.Core/Crypto/Curve25519.cs`, a X25519 of our own checked against
the vectors of RFC 7748. The public key is never taken from the caller: the store counts it from the
private one on every write, so the pair in the database always belongs together. The preshared key is
32 bytes of `RandomNumberGenerator`, and the panel asks for it by the button beside the field.

## The parts

| Where | Holds |
|---|---|
| `AmneziaGeo.Server.Core/Crypto/Curve25519.cs` | the keys |
| `AmneziaGeo.Server.Awg/Config/ServerConfig.cs` | the settings and the obfuscation |
| `AmneziaGeo.Server.Awg/Config/ConfigDefaults.cs` | a fresh configuration |
| `AmneziaGeo.Server.Awg/Config/ConfigRules.cs` | what is refused and under which code |
| `AmneziaGeo.Server.Awg/Config/ConfigDevice.cs` | the settings turned into a kernel update |
| `AmneziaGeo.Server.Dal/ConfigStore.cs` | the database side |
| `AmneziaGeo.Server.Api/Configs/ConfigEndpoints.cs` | the routes |
| `amneziageo-web/src/pages/Configs.tsx` | the page |
| `amneziageo-web/src/components/ConfigForm.tsx` | the form |
