# The balancers

A balancer selects from registered linked servers under one name. An ordered routing rule chooses it as its
processing method, then the connection uses a member according to its strategy. Linked-server and balancer names
share a namespace. The settings open on a separate page under **Routing → Balancers**.

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
| Linked servers | up to 16 registered connections, in the order they are taken |
| DNS | optional default resolvers; empty uses the DNS of the selected linked server |
| On | whether the rules that name the balancer go on the host |

Renaming a balancer updates its processing rules. A referenced balancer is not removed until those rules stop
using it; the response is `balancer-in-use`. Its settings show the referencing rules as links. Members can be added,
removed and reordered, and existing local paths remain available to preserve earlier configurations.

An address set in Default DNS mode uses this balancer's DNS when the field is populated, otherwise the DNS of its
selected member. A custom address-set policy uses its own resolvers and any migrated member overrides instead.
Questions travel through the selected member in either case. Priority preview can name the first available member;
round and sticky previews show the possible live member DNS values, since selection can vary by question or client.
Missing member DNS or an unavailable balancer does not switch to an unrelated resolver or direct access.

## The strategies

`priority` gives the traffic to the first outbound of the list that carries traffic, and moves to the
next one when it stops answering. `round` hands the connections to the live outbounds one after another,
through `numgen inc`. `sticky` picks by the address of the client, through `jhash ip saddr` under a seed the
balancer keeps, so one client stays on one outbound while the set of live members does not change. The seed
comes from the number of the balancer, which means it survives every write of the ruleset and every restart
of the panel; without it nftables takes a fresh random seed each time the ruleset is loaded and the clients
change places.

## What carries traffic

An outbound counts while its probe gets through: a question to a name server sent under the mark of the
outbound, with the address and the period held by the outbound itself, see [outbounds.md](outbounds.md).
Two probes that miss in a row take a member off, and one answer brings it back. Until an outbound is
probed the handshake stands in: an outbound through the host itself always counts, and a tunnel counts
while its last handshake is under three minutes old. The server reads this back every fifteen seconds, and
when the set of live outbounds is other than before it lays the whole ruleset again, so a member that went
quiet is replaced without anyone touching the panel. A change made in the panel does not wait for that round:
laying the rules reads the members off the host first, so an outbound turned off, removed or taken out of a
group leaves the ruleset at once. An outbound that was turned on joins it as soon as it carries traffic, which
takes a handshake or a probe.

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

An outbound the panel no longer holds is passed over, and one that is turned off is not picked. Both codes
say the way out is gone rather than the rule is wrong, so the rule drops what it matches until a member
carries again; see the reasons in `rules.md`.

## What the panel shows

Members, strategy and DNS are edited on a separate page. The DNS preview follows the current draft and available
member metadata. Save and Cancel return to the originating list with its filter; a balancer opened from an
unfinished rule returns to that retained rule.

The section names the balancer, its strategy, the outbounds it picks from with the live ones marked, and
whether it is on the host. A balancer a rule leaves through is not removed until the rule is changed.
