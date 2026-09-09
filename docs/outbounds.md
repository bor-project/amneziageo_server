# The ways out

An outbound is a way traffic leaves the host: through the uplink of the host itself, through an
AmneziaWG tunnel the host holds as a client of another server, or through such a tunnel carried inside a
websocket to a wstunnel proxy. Every outbound carries a mark of its own;
a packet marked with it is routed out through that outbound. The panel keeps the outbounds in SQLite and
puts them on the host through the `ip` and `nft` tools.

## Rights

| Route | Right |
|---|---|
| `GET /api/outbounds` | `state:read` |
| `GET /api/outbounds/{id}` | `state:read` |
| `GET /api/outbounds/draft` | `routing:write` |
| `POST /api/outbounds/keys` | `routing:write` |
| `POST /api/outbounds/import` | `routing:write` |
| `POST /api/outbounds` | `routing:write` |
| `PUT /api/outbounds/{id}` | `routing:write` |
| `POST /api/outbounds/{id}/switch` | `routing:write` |
| `POST /api/outbounds/{id}/move` | `routing:write` |
| `POST /api/outbounds/{id}/apply` | `routing:write` |
| `POST /api/outbounds/apply` | `routing:write` |
| `DELETE /api/outbounds/{id}` | `routing:write` |

`routing:write` is the same right the geo databases are managed under, and it counts as sensitive: a
caller without a fresh login is refused. Keys are answered only to a caller that holds it.

## The kinds

| Kind | Carries |
|---|---|
| `local` | the uplink of the host, no interface of its own |
| `wg` | an AmneziaWG interface the host raises as a client of another server |
| `ws` | the same interface, with its UDP carried inside a websocket to a wstunnel proxy |

A fresh database is seeded with one `local` outbound named `direct`.

## Through a websocket

A `ws` outbound is for a network that carries web traffic and nothing else. The panel binds a loopback
port, points the interface at it, and every datagram travels to the proxy as one websocket message; the
proxy hands it to the server on its own loopback, at the port the outbound names. The websocket opens on
the first datagram and is opened again after a drop.

| Setting | Holds |
|---|---|
| Websocket proxy | the proxy, as `proxy.example.net` or `wss://user:secret@proxy.example.net:8443/token` |
| Address, Port | the server behind the proxy, and the port the proxy hands the tunnel to |

A bare name takes the port of the server; an address carries the port, the path token and the basic-auth
credentials. Changing the proxy opens a new carrier and drops the old one; turning the outbound off takes
the carrier down. Every other setting is the one a `wg` outbound carries.

## The settings of a tunnel

| Setting | Holds |
|---|---|
| Name | the name of the interface, up to 15 characters |
| Address, Port | where the server is reached |
| Interface address | the ranges the interface carries, as the server handed them out |
| Name servers | the servers reachable through the tunnel |
| MTU, Keepalive | the packet size and how often the path is held open |
| Private key | the key of the interface; the public one is derived from it |
| Server public key | the key the server is known by |
| Preshared key | the key added on top of the handshake, when the server asks for one |
| Obfuscation | the AmneziaWG shape, which has to match the server exactly |

A tunnel with no obfuscation at all is a plain WireGuard tunnel, and nothing is sent to the kernel about
it. A tunnel that carries some of it is checked the way an endpoint is.

## Reading a client configuration

`POST /api/outbounds/import` takes the `.conf` an AmneziaWG server hands its clients and answers with the
outbound it carries: keys, address, obfuscation and endpoint. Nothing is stored: the answer is a draft the
panel fills the form with. In the panel the same thing is the field at the bottom of the form.

## The mark, the table and the rule

Every outbound is given the first free mark from `0xA601` up, and a routing table of its own from `42601`
up. A `local` outbound looks the way out up in the main table instead. What lands on the host is:

- `ip rule add pref 10000+n fwmark <mark> lookup <table>`, above the main rule, so an unmarked packet
  keeps going the way it went before;
- `default dev <name> table <table>` for a tunnel;
- one firewall table, `inet amneziageo_out`, that masquerades what leaves through every outbound that is
  on and holds a TCP segment down to what the path carries.

The interface itself is given no mark: the mark is what sends a packet into the tunnel, not what the
tunnel puts on its own packets, so there is no loop to break.

## Putting it on the host

Adding, changing, turning on and off, and removing an outbound each put it on the host straight away.
`POST /api/outbounds/apply` goes over every outbound and rewrites the firewall table, which is what the
`Apply all` button in the panel does. An outbound that is turned off is taken off the host: the rule goes,
the table is cleared, and the interface is removed.

Raising an interface, writing keys to it and changing routing rules need `CAP_NET_ADMIN`. A server that
runs without it answers with the outbound and the reason on it instead of failing the request. Setting
`Routing:Sudo` to true runs `ip` and `nft` through `sudo -n`.

## What the panel shows

The table names the kind, the server, the mark and the table, the last handshake and the traffic each way.
A `ws` outbound names the server behind the proxy, and the proxy itself stands in the form.
A tunnel counts as alive while its handshake is under three minutes old.
