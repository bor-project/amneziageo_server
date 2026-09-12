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
| `POST /api/outbounds/{id}/probe` | `routing:write` |
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
| Close private networks | whether a client is kept out of the private ranges behind the tunnel, on for a new tunnel |
| Private key | the key of the interface; the public one is derived from it |
| Server public key | the key the server is known by |
| Preshared key | the key added on top of the handshake, when the server asks for one |
| Obfuscation | the AmneziaWG shape, which has to match the server exactly |

A tunnel with no obfuscation at all is a plain WireGuard tunnel, and nothing is sent to the kernel about
it. A tunnel that carries some of it is checked the way an endpoint is.

## The probe

Every outbound carries a probe of its own: the name server it asks over UDP, and how often. The question
goes out under the mark of the outbound, so it takes the way out the rules take, and an answer means that
way is open. The panel asks for `example.com` and waits two seconds.

| Setting | Holds |
|---|---|
| Name server | the server the probe asks, empty for no probe |
| Every, seconds | how often the probe goes out, from 5 to 3600 |

Two probes that miss in a row take the outbound off the balancers, and one answer brings it back. A probe
that cannot be marked, which is what happens without `CAP_NET_ADMIN`, leaves the verdict as it was, and the
age of the handshake stands in again.

`POST /api/outbounds/{id}/probe` sends one probe at once and answers with what the host holds for the
outbound. The panel does the same under `Probe now` in the row menu.

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
  on, holds a TCP segment down to what the path carries and filters what comes out of the tunnels.

The interface itself is given no mark: the mark is what sends a packet into the tunnel, not what the
tunnel puts on its own packets, so there is no loop to break. The interface takes each of its addresses
alone, as `/32` or `/128`, whatever mask the server handed it with, so the network behind the tunnel is
reached through the rules and never through a route of the main table.

## What a tunnel lets back

The server at the other end of a tunnel is trusted with what the host sends into it and with nothing
else. Out of a tunnel the host takes only what answers a connection it made or passed on: a new
connection that comes out of a tunnel is dropped, both on its way to the host and on its way through it.
Nor does the host pass on what came in on the uplink and would leave through it again, so the
masquerading of a `local` outbound covers the clients and the host alone.

```
chain input {
    type filter hook input priority filter; policy accept;
    iifname { "awgbor" } ct state established,related accept
    iifname { "awgbor" } drop
}
```

A tunnel with **Close private networks** on also rejects a new connection a client opens through it to
a private range: `10.0.0.0/8`, `100.64.0.0/10`, `169.254.0.0/16`, `172.16.0.0/12`, `192.168.0.0/16`,
`fc00::/7` and `fe80::/10`, which is where the tunnel network of the server and whatever stands behind it
live. With it off, a rule that sends such a range into the tunnel takes the client there, under the
address of the tunnel.

## Putting it on the host

Adding, changing, turning on and off, and removing an outbound each put it on the host straight away.
`POST /api/outbounds/apply` goes over every outbound, rewrites the firewall table and lays the rules again;
the panel does the same when it starts, so the outbounds and the rules come back after a reboot. An interface
that already carries the server of its outbound and no other peer keeps it, with the handshake and the
counters, so the panel starting over does not break the tunnel. An outbound that is turned off is taken off the
host: the rule goes, the table is cleared, and the interface is removed.

Raising an interface, writing keys to it and changing routing rules need `CAP_NET_ADMIN`. A server that
runs without it answers with the outbound and the reason on it instead of failing the request. Setting
`Routing:Sudo` to true runs `ip` and `nft` through `sudo -n`.

## What the panel shows

The table names the kind, the server, the mark and the table, the last handshake and the traffic each way.
A `ws` outbound names the server behind the proxy, and the proxy itself stands in the form.
A tunnel counts as alive while its handshake is under three minutes old.
