# Routing rules and address sets

The routing menu contains **Rules**, **Addresses**, **Linked servers** and **Balancers**. In Russian these are
**Правила**, **Адреса**, **Связанные серверы** and **Балансировщики**. Rules are evaluated in table order. An address set
contains reusable destination conditions and DNS settings; a rule selects one or more address sets and a processing
method. Register a linked server or a balancer, create the required address sets, and select them in a rule.

The rules, address sets and processing objects have separate settings pages. Their names in the table link to those
pages. Saving or cancelling returns to the originating list with its filter; creating or editing an address set
from a rule retains the unfinished rule and selects a newly created set when returning.

## Rights and routes

| Route | Right |
|---|---|
| `GET /api/traffic-rules`, `GET /api/traffic-rules/{id}` | `state:read` |
| `GET /api/traffic-rules/dns-defaults` | `state:read` |
| `GET /api/traffic-rules/draft` | `routing:write` |
| `POST /api/traffic-rules`, `PUT /api/traffic-rules/{id}` | `routing:write` |
| `POST /api/traffic-rules/defaults`, `DELETE /api/traffic-rules/{id}` | `routing:write` |
| `GET /api/routing-entries`, `GET /api/routing-entries/table`, `GET /api/routing-entries/{id}` | `state:read` |
| `POST /api/routing-entries/test` | `state:read` |
| `GET /api/routing-entries/draft`, `GET /api/routing-entries/ruleset` | `routing:write` |
| `POST /api/routing-entries`, `PUT /api/routing-entries/{id}` | `routing:write` |
| `POST /api/routing-entries/{id}/switch`, `POST /api/routing-entries/{id}/move` | `routing:write` |
| `POST /api/routing-entries/apply`, `DELETE /api/routing-entries/{id}` | `routing:write` |

The API calls address sets `traffic-rules` and ordered processing rules `routing-entries`. The public panel paths are
`/routing/rules` for ordered rules and `/routing/addresses` for address sets. Existing links to
`/routing/rules/{id}/edit` continue to edit the same ordered rule, not an address set with a similar number.

## Address sets

| Setting | Holds |
|---|---|
| Name | the name of the address set, up to 32 characters |
| Addresses and domains | destination networks, domains, keywords and geo keys |
| DNS | default from the processing method, a named preset, or custom resolver addresses |

`geoip:ru` selects a country and `geosite:youtube` a category. `203.0.113.0/24` and `8.8.8.8` are address ranges;
`example.org` and `domain:example.org` are domains; `cidr:10.0.0.0/8` explicitly names a range. `keyword:ads` matches
names containing that word. Keywords and domain categories obtain their addresses from the resolver or from names
read from connections; they do not supply static address ranges. An address set takes up to 1024 targets.

A new address set requires at least one address or domain. An empty catch-all carried over from an earlier release
keeps its existing meaning. Editing the DNS of such a migrated set does not silently change its scope.

The same set may be selected by several rules without copying its destinations or DNS policy. The settings page
shows its use by those rules and the effective DNS for each processing method. A referenced set cannot be deleted
until its rules stop using it. Restoring the standard address sets adds missing definitions and does not activate
processing rules or overwrite edited definitions.

## DNS of an address set

**Default** is a live reference to the selected processing method:

- a linked server supplies its saved DNS, including DNS imported from its client configuration;
- a balancer supplies its own DNS when configured, otherwise the DNS of the member selected for the question;
- direct processing uses the host DNS reported by `GET /api/traffic-rules/dns-defaults`;
- blocking does not forward the question.

Changing a linked server or balancer updates this inherited choice without copying its addresses into every set.
Missing DNS or an unavailable processing object does not select a different public resolver or a direct path.
Priority balancing shows the first available member. Round and sticky balancing can use different members, so the
panel shows possible member DNS values rather than claiming that one server is always selected.

Cloudflare, Google and Quad9 presets store explicit DNS addresses as `dnsMode: "custom"`; custom mode also accepts
the supported plain DNS, DNS-over-TLS and DNS-over-HTTPS forms described in [dns.md](dns.md). The address syntax
determines the resolver transport: a plain IP preset does not itself enable TLS or HTTPS. Choosing a preset replaces
previous member overrides. `dnsMode: "default"` uses the processing object and ignores explicit DNS and member maps.

Migrated custom policies may contain `dnsMembers`, resolver addresses for particular balancer members. These remain
visible when custom DNS is selected. Within a rule containing overlapping address sets, the first matching set in
the selected order supplies DNS; the arrows in the rule settings change that order.

## Ordered rules

| Setting | Holds |
|---|---|
| Name | what the rule is shown under, up to 32 characters |
| Processing | a linked server, a balancer, direct access, or blocking |
| Addresses | one or more reusable address sets, in their selected order |
| Clients | client names; empty selects every client |
| Interfaces | incoming interface names; empty selects every interface |
| Destination ports | single ports and inclusive ranges such as `443` or `1000-2000` |
| Source ports | the ports the traffic comes from, in the same form |
| Protocol | `any`, `tcp` or `udp` |
| Enabled | whether the rule participates in routing |

Targets from the selected sets are alternatives: matching any selected set satisfies the destination condition.
Client, incoming-interface, protocol and port filters are additional conditions on the rule. They are not stored
in the reusable sets. Up to 64 conditions of each filter kind are accepted. Renaming a client or interface updates
the rules naming it. Removing the last named client or interface disables the affected rule instead of broadening
it to everyone; removing one of several names retains the remaining filter.

Rules are evaluated from top to bottom and the first match decides. `move` with `{"up": true}` or `{"up": false}`
swaps a rule with its neighbour. `{"to": 3}` places it third, counting from one, and renumbers the others; a position
beyond the end places it last. Dragging is available on an unfiltered table, and arrow buttons also change priority.
Disabled rules do not participate. Traffic matching no rule takes the host's ordinary route.

DNS chooses the first rule matching the queried name, client and incoming interface. Source ports, destination
ports and transport protocol filter application traffic, but are ignored for DNS selection: a DNS question does
not contain the port or protocol of a future connection. The DNS question travels through the processing object
selected by that rule. There is no independent DNS route selector on another page.

## What the host takes

The compiled rules are loaded together into `inet amneziageo_rt` with `nft -f -`. Each ordered rule has IPv4 and IPv6
interval sets `r<id>v4` and `r<id>v6`, plus timeout name sets `n<id>v4` and `n<id>v6`. The sets contain the combined
destinations of the rule's address sets. Names and cached addresses are retained across ruleset replacement; see
[dns.md](dns.md#the-sets-of-the-rules). Set identifiers refer to ordered rule IDs.

The prerouting chain limits routing to the panel's incoming interfaces, drops invalid connection-tracking state,
restores connection marks and evaluates new connections. Client and incoming-interface filters become source-address
and `iifname` matches; source and destination ports remain separate matches. The first packet chooses the processing
method, and its connection mark keeps subsequent packets on that path. Changing a rule does not move established
connections. A newly added blocking rule blocks new connections; already established ones retain their path.

Direct processing returns without an outbound mark. Linked servers and balancers supply the mark described in
[outbounds.md](outbounds.md) and [balancers.md](balancers.md). Reply packets do not inherit the outbound mark and use
the host's route back to the client. Connection logging and name inspection keep their existing nftables hooks;
see [diagnostics.md](diagnostics.md) and [dns.md](dns.md#names-read-out-of-the-connections).

## Unavailable processing objects

| Reason | Means |
|---|---|
| `unknown-outbound` | the selected object is absent |
| `outbound-off`, `balancer-off` | it is disabled |
| `outbound-down`, `no-live-member` | the linked server, or every suitable balancer member, is unavailable |
| `empty-target` | destination expansion produces nothing usable |
| `unknown-client`, `unknown-inbound` | none of the named clients or incoming interfaces remains available |

An otherwise valid rule whose selected exit is unavailable holds its matching traffic instead of sending it through
the host. DNS also fails on that selected path; it does not fall back directly. Invalid destination conditions are
reported separately. Routing requires at least one incoming interface.

## The route tester

`POST /api/routing-entries/test` accepts, for example,
`{"target":"example.org","port":443,"protocol":"tcp","client":"phone","sourcePort":null}`. It evaluates the
current compiled rules and reports the deciding rule, its position, selected exit, balancer members, resolved
addresses and the rules examined before the verdict. The verdict is `out`, `host`, `block`, `held`, `guard` or `dns`.
The panel opens the tester from Rules or at `/routing/test`. The resolver's interception and DNS guards remain
ahead of ordinary forwarding rules, as described in [dns.md](dns.md).

## Migration and existing configurations

The upgrade moves address groups into reusable address sets, and moves inline destinations into referenced sets.
Ordered rule IDs, priority, enabled state, processing objects, client and incoming-interface filters, ports and
protocols are retained. Previous basic direct/block lists become ordinary rules at their previous priority.
Client keys, client configurations, subscriptions and their public identifiers are not recreated by this migration.
Historical migrations remain available to upgrade earlier installations; obsolete active routing and independent
DNS-policy entities are replaced by the new model.

Address-group IDs and every rule's references to those sets are retained. If a shared set previously had different
effective DNS policies in different rules, the upgrade keeps one shared set and selects **Default** DNS from each
rule's processing method. The resolver addresses used by those rules can change in this case. Without such a
conflict, the previous resolver, linked-server and balancer DNS addresses remain in migrated custom policies and
member overrides.

Old Global/Outbound settings could select a different DNS exit from application traffic; DNS now follows the
processing object of the matching rule. An old DNS balancer that differs from that object contributes its configured
addresses as an ordered resolver list, rather than preserving a separate DNS scheduler.

### Upgrade and downgrade

Previously independent DNS exits are replaced by the processing path of the matching rule. Migrated DNS addresses
are retained except when a shared set has conflicting policies; that set inherits DNS from each rule's processing
method. The Addresses usage view shows the resulting effective DNS.
After this schema migration, downgrading the application also requires restoring the matching database backup
created before the upgrade. An older binary must not be started against the migrated database alone.

## Taking routing to another server

| Route | Right |
|---|---|
| `GET /api/routing/export` | `state:read` |
| `POST /api/routing/import`, `POST /api/routing/import/apply` | `routing:write` |

Export format 4 contains address definitions in `rules` (`name`, `targets`, `dnsMode`, `dns`, `dnsMembers`), ordered
processing rules in `entries` (including address-set names in `rules`, filters and processing), routing presets and
geo-source addresses. Linked servers, balancers and DNS service listener/cache settings remain specific to each
server. Imported rules with missing processing objects retain their references and cannot use those objects until
they are registered.

Import preview reports definitions and processing rules that will be added or replaced, plus unresolved references.
Applying the same file updates matching names and leaves objects absent from the file unchanged. Earlier formats
1–3 remain importable through conversion to the current model; they are not exported again as obsolete objects.
The utility provides `amneziageo-server routing export <file>` and `routing import <file>`, with `--apply` to apply.
