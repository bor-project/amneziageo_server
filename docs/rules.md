# The routing rules

A rule says where traffic goes: it matches what the client asks for and either sends it out through an
outbound, lets it out the way the host sends its own, or drops it. The rules are held in SQLite, in the order they are read in, and the whole set is
turned into one nftables ruleset the host takes at once.

## Rights

| Route | Right |
|---|---|
| `GET /api/rules` | `state:read` |
| `GET /api/rules/{id}` | `state:read` |
| `GET /api/rules/basic` | `state:read` |
| `POST /api/rules/test` | `state:read` |
| `GET /api/rules/draft` | `routing:write` |
| `GET /api/rules/ruleset` | `routing:write` |
| `POST /api/rules` | `routing:write` |
| `PUT /api/rules/{id}` | `routing:write` |
| `POST /api/rules/{id}/switch` | `routing:write` |
| `POST /api/rules/{id}/move` | `routing:write` |
| `DELETE /api/rules/{id}` | `routing:write` |
| `POST /api/rules/apply` | `routing:write` |
| `PUT /api/rules/basic` | `routing:write` |

## The settings of a rule

| Setting | Holds |
|---|---|
| Name | what the rule is shown under, up to 32 characters |
| Action | `out` to send the traffic through an outbound, `direct` to let it out the way the host sends its own, `block` to drop it |
| Outbound | the name of the outbound or the balancer the traffic leaves through |
| Where to | geo keys, domains and address ranges the traffic goes to |
| Where from | the client addresses and ranges the traffic comes from |
| Clients | the clients of the panel the traffic comes from, by name |
| Interfaces | the interfaces of the panel the traffic comes in on |
| Ports | single ports and ranges written as `443` or `1000-2000` |
| Source ports | the ports the traffic comes from, written the same way |
| Protocol | `any`, `tcp` or `udp` |
| Hold the traffic while the channel is down | whether the rule drops what it matches instead of letting it out through the host |
| On | whether the rule goes on the host |

A rule that is added holds its traffic and is off unless the request says otherwise, and a change that says
nothing about either keeps what the rule already carries, so a client written before the settings existed
neither takes the hold off nor turns the rule off by touching it. The clients, the interfaces and the source ports are kept the same way:
a change that does not name them leaves them as they are.

A client named by a rule brings its addresses into the addresses the rule matches by, next to the ones written
by hand; the names are read without regard to case. The rule is laid on the host again whenever a client is
added, changed or removed, so a new address of a named client is matched without touching the rule. A name the
panel no longer holds is passed over; a rule none of
whose clients the panel holds stays off the host with `unknown-client`. An interface is the name of a
configuration; a rule naming interfaces matches only the traffic that came in on one of them, and a rule none
of whose interfaces the panel holds stays off the host with `unknown-inbound`. Renaming a client or a
configuration writes the new name into every rule that names it, so the rule goes on matching the same
traffic; a client added later under a name a rule used to carry is not taken into it. An outbound or a
balancer renamed is written into the rules the same way, see `outbounds.md` and `balancers.md`.

A target is read as what it looks like: `geoip:ru` is a country, `geosite:youtube` a category,
`1.2.3.0/24` and `8.8.8.8` are ranges, and anything else with a dot in it is a domain. A domain is also taken
as `domain:ifconfig.me`, the form the console client of AmneziaGeo writes its lists in, so one list carries
over to the other side unchanged. `keyword:ads` matches every name the word occurs in, as in 3x-ui; a keyword
brings no range of its own, so the addresses of a rule that carries one arrive only from the answers the
resolver sees. Ranges come from `geoip:` and from the networks written into the targets; `geosite:` gives
names. A rule with no targets matches every address, a rule with no client
addresses matches every client.

## The order

Rules are read from the top down and the first one that matches decides. `move` with `{"up": true}` or
`{"up": false}` swaps a rule with its neighbour; `move` with `{"to": 3}` puts it at the third place, counting
from one, and numbers the rest anew. A place past the end puts the rule last. A rule that is off is left out
of the ruleset entirely.

## The basic lists

Two lists stand ahead of every rule: `Block` drops what it matches and `Direct` lets it out the way the host
sends its own, without a mark. Each list takes up to 1024 targets written the same way as the targets of a
rule, and block is read before direct. An empty list takes no line at all. `GET /api/rules/basic` returns both
lists with what the host carries for them; `PUT /api/rules/basic` takes `{"direct": [...], "block": [...]}`
and keeps the list it does not name. The lists live in the plan under the numbers `-1` for block and `-2` for
direct, so their sets are named `rb1v4`, `nb1v4`, `rb2v4`, `nb2v4` and so on, and the resolver fills the name
sets of the lists the same way it fills the sets of the rules.

## What the host takes

The rules go into the table `inet amneziageo_rt`, written whole and loaded through `nft -f -`, so the
host swaps one ruleset for the next in a single step. The table carries:

- one interval set of ranges per rule and family, named `r<id>v4` and `r<id>v6`, filled from the geo
  databases;
- one timeout set per rule and family, named `n<id>v4` and `n<id>v6`, that the resolver fills with the
  addresses the names of the rule answered with, both from the questions of the clients and from the questions
  it asks about the names of the rule itself, see [dns.md](dns.md);
- a `prerouting` chain at mangle priority that leaves alone anything that did not arrive on an endpoint of
  the panel, drops a packet of a client that belongs to no connection the host tracks (`ct state invalid`),
  restores the mark of a connection that already has one, and hands only a new connection to the decision
  chain;
- a `decide` chain that carries the basic lists and then the rules themselves.

A rule naming interfaces starts each of its lines with `iifname { ... }`, a rule naming clients matches their
addresses with `ip saddr` and `ip6 saddr` next to the ranges written by hand, and source ports go into
`th sport` or `tcp sport` and `udp sport` ahead of the destination ports.

The rules decide once, on the first packet of a connection. A rule that sends traffic out sets the mark of
its outbound and the mark is kept on the connection; a connection no rule took goes on the way the host
picked for it. Either way a connection stays on its path even after the rules change or its address reaches
the set of a rule later. The mark itself is what the `ip rule` of the outbound looks up: the routing tables
and the marks come from the outbounds and are described in [outbounds.md](outbounds.md). A rule that blocks
drops a new connection; one that was open before the rule came goes on until it ends. A direct rule returns
from the decision without a mark, so the connection takes the way out the host picks and no rule below it is
read.

The mark comes back only to what the clients send. An answer that returns through an outbound carries no
mark, so the host finds the client in its own tables instead of sending the answer back into the outbound.

Traffic that matches no rule carries no mark and takes the way out the host itself picks.

## What stays off the host

A rule goes into the ruleset only when everything it names is there. The panel shows the reason
otherwise:

| Reason | Means |
|---|---|
| `unknown-outbound` | the rule names an outbound the panel does not hold |
| `outbound-off` | the outbound of the rule is turned off |
| `outbound-down` | the outbound of the rule carries nothing: its probe does not get through |
| `empty-target` | the geo databases carry nothing for what the rule matches by |
| `unknown-client` | the panel holds none of the clients the rule names |
| `unknown-inbound` | the panel holds none of the interfaces the rule names |

The first three say the rule itself is good and only its way out is gone, and a balancer whose members all
carry nothing says the same with `no-live-member`. `Hold the traffic while the channel
is down` decides what happens then, and a fresh rule holds: the rule still goes into the ruleset, but it
drops what it matches instead of marking it, and the panel shows `traffic held` with the reason behind it.
Turn the holding off and the traffic leaves through the way out of the host, under the address of the server
itself, which is what a client behind a channel does not expect. `empty-target` is the rule being wrong
rather than its channel being down, so a rule with it is never held: it simply stays off the host.

Rules also need at least one endpoint: without a configuration to arrive on, nothing is marked at all.

## The route tester

`POST /api/rules/test` takes `{"target": "youtube.com", "port": 443, "protocol": "tcp", "client": "bor",
"sourcePort": null}` and reads the rules the host carries the way the ruleset does. The target is a name, an
address or a link whose host is taken; a name is asked about through the way out of the resolver, or through
the host when the resolver is off. The client is a client of the panel, whose addresses and interface the
traffic then carries, or a bare address, whose interface is the configuration its range belongs to. The
protocol is `tcp` or `udp`, both ports may be left out.

The guards of the resolver come first: with DoT blocked, port 853 is dropped, and with DoH blocked, port 443
to a known name server is dropped. Next comes the resolver itself: while it intercepts, every question on port
53 that comes in on a client interface is answered by it, and the rules are not read at all. Then each rule is
read in order: a rule off the host is passed over with its reason, a rule whose interface, client, protocol,
port or source port does not fit is missed, and a rule
whose targets take the traffic matches by a range, by the name or by an address the resolver laid into its
set. The answer carries the verdict (`out`, `host`, `block`, `held`, `guard` or `dns`), the rule that decided with
its place, the way out with the members of a balancer and which of them carry, the addresses the name
resolved to and every rule read before the decision. Each member of the way out says whether it is on
(`isEnabled`), whether it carries traffic at all (`carries`, the same measure the outbound itself reports)
and whether this rule sends traffic to it (`isPicked`).

## What the panel shows

The routing section carries the rules in their order with their number, a switch that turns each one on and
off, what each one matches and sends the traffic to, how many ranges and names it came out as, and whether it
is on the host; a rule is moved by dragging its row while the table is neither sorted nor filtered. A balancer
is marked as a group. The `Basic` tab edits the basic lists, the `Test` tab runs the route tester and the
`nftables` tab shows the ruleset as the host takes it. The panel lays the ruleset when it starts, after every
change and when the live outbounds change; `GET /api/rules/ruleset` returns it as it is written.
