# The routing rules

A rule says where traffic goes: it matches what the client asks for and either sends it out through an
outbound or drops it. The rules are held in SQLite, in the order they are read in, and the whole set is
turned into one nftables ruleset the host takes at once.

## Rights

| Route | Right |
|---|---|
| `GET /api/rules` | `state:read` |
| `GET /api/rules/{id}` | `state:read` |
| `GET /api/rules/draft` | `routing:write` |
| `GET /api/rules/ruleset` | `routing:write` |
| `POST /api/rules` | `routing:write` |
| `PUT /api/rules/{id}` | `routing:write` |
| `POST /api/rules/{id}/switch` | `routing:write` |
| `POST /api/rules/{id}/move` | `routing:write` |
| `DELETE /api/rules/{id}` | `routing:write` |
| `POST /api/rules/apply` | `routing:write` |

## The settings of a rule

| Setting | Holds |
|---|---|
| Name | what the rule is shown under, up to 32 characters |
| Action | `out` to send the traffic through an outbound, `block` to drop it |
| Outbound | the name of the outbound the traffic leaves through |
| Where to | geo keys, domains and address ranges the traffic goes to |
| Where from | the client addresses and ranges the traffic comes from |
| Ports | single ports and ranges written as `443` or `1000-2000` |
| Protocol | `any`, `tcp` or `udp` |
| On | whether the rule goes on the host |

A target is read as what it looks like: `geoip:ru` is a country, `geosite:youtube` a category,
`1.2.3.0/24` and `8.8.8.8` are ranges, and anything else with a dot in it is a domain. A rule with no
targets matches every address, a rule with no client addresses matches every client.

## The order

Rules are read from the top down and the first one that matches decides. `move` swaps a rule with its
neighbour. A rule that is off is left out of the ruleset entirely.

## What the host takes

The rules go into the table `inet amneziageo_rt`, written whole and loaded through `nft -f -`, so the
host swaps one ruleset for the next in a single step. The table carries:

- one interval set of ranges per rule and family, named `r<id>v4` and `r<id>v6`, filled from the geo
  databases;
- one timeout set per rule and family, named `n<id>v4` and `n<id>v6`, that the resolver fills with the
  addresses the names of the rule answered with;
- a `prerouting` chain at mangle priority that restores the mark of a connection that already has one,
  leaves alone anything that did not arrive on an endpoint of the panel, and hands the rest to the
  decision chain;
- a `decide` chain that carries the rules themselves.

A rule that sends traffic out sets the mark of its outbound, and the mark is kept on the connection, so
a connection stays on the path it was given even after the rules change. The mark itself is what the
`ip rule` of the outbound looks up: the routing tables and the marks come from the outbounds and are
described in [outbounds.md](outbounds.md). A rule that blocks drops the packet.

Traffic that matches no rule carries no mark and takes the way out the host itself picks.

## What stays off the host

A rule goes into the ruleset only when everything it names is there. The panel shows the reason
otherwise:

| Reason | Means |
|---|---|
| `unknown-outbound` | the rule names an outbound the panel does not hold |
| `outbound-off` | the outbound of the rule is turned off |
| `empty-target` | the geo databases carry nothing for what the rule matches by |

Rules also need at least one endpoint: without a configuration to arrive on, nothing is marked at all.

## What the panel shows

The routing page carries the rules in their order, what each one matches and sends the traffic to, how
many ranges and names it came out as, and whether it is on the host. The panel lays the ruleset when it
starts, after every change and when the live outbounds change; `GET /api/rules/ruleset` returns it as it is
written.
