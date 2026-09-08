# The balancers

A balancer is a group of outbounds under one name. A rule names it the way it names an outbound, and the
traffic leaves through one of the members: the first one that answers, the next one in turn, or the one
the address of the client falls on. The names of the outbounds and of the balancers live in one list, so
a balancer cannot take a name an outbound already carries.

## Rights

| Route | Right |
|---|---|
| `GET /api/balancers` | `state:read` |
| `GET /api/balancers/{id}` | `state:read` |
| `GET /api/balancers/draft` | `routing:write` |
| `POST /api/balancers` | `routing:write` |
| `PUT /api/balancers/{id}` | `routing:write` |
| `POST /api/balancers/{id}/switch` | `routing:write` |
| `DELETE /api/balancers/{id}` | `routing:write` |

## The settings of a balancer

| Setting | Holds |
|---|---|
| Name | what the rules call the balancer by, up to 32 characters |
| Strategy | `priority`, `round` or `sticky` |
| Outbounds | up to 16 outbounds, in the order they are taken |
| On | whether the rules that name the balancer go on the host |

## The strategies

`priority` gives the traffic to the first outbound of the list that carries traffic, and moves to the
next one when it stops answering. `round` hands the connections to the live outbounds one after another,
through `numgen inc`. `sticky` picks by the address of the client, through `jhash ip saddr`, so one
client stays on one outbound while the set of live members does not change.

## What carries traffic

An outbound that leaves through the host itself always counts. A tunnel counts while its last handshake
is under three minutes old, which is read from the kernel together with the counters of the peer. The
server reads this back every fifteen seconds, and when the set of live outbounds is other than before it
lays the whole ruleset again, so a member that went quiet is replaced without anyone touching the panel.

## What the host takes

A rule that leaves through a balancer with one live member sets its mark, exactly as a rule that names
an outbound. With more than one member the mark comes out of a map:

```
ip daddr @r1v4 meta mark set numgen inc mod 2 map { 0 : 0xa602, 1 : 0xa601 } return
ip daddr @r1v4 meta mark set jhash ip saddr mod 2 map { 0 : 0xa602, 1 : 0xa601 } return
```

`sticky` needs the family of the address, so a rule that matches every address takes two lines, one
under `meta nfproto ipv4` and one under `meta nfproto ipv6`. The mark is kept on the connection the same
way, so a connection stays on the outbound it was given even when the next packet would fall elsewhere.

## When a rule stays off the host

| Code | Means |
|---|---|
| `balancer-off` | the balancer is turned off |
| `no-live-member` | nothing the balancer picks from is enabled and answering |

An outbound the panel no longer holds is passed over, and one that is turned off is not picked.

## What the panel shows

The section names the balancer, its strategy, the outbounds it picks from with the live ones marked, and
whether it is on the host. A balancer a rule leaves through is not removed until the rule is changed.
