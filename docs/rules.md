# Routing rules

**Routing** has four tabs: **Routing**, **Linked servers**, **Balancers** and **Proxies**. Use **Add** on the relevant tab or open a row by its name. The edit page returns to the originating table on save or cancel. Its breadcrumbs also return to the parent and ask before discarding unsaved changes.

A row defines its destinations, filters, processing method and DNS. Rules are evaluated in table order. The pinned **Default** row handles traffic that matches none of the entries above it. A new installation uses the host route and host DNS. The row always stays enabled and last.

The rule editor accepts the [supported destination formats](#destinations) and can insert Geo categories. DNS can inherit from the selected route, use a preset or contain explicit addresses. The route tester and routing exchange are available on the first tab. The Geo sources and resolver service settings are available from the routing tools.

## Internet only

In the **Routing** tab, open **Presets**, choose **Internet only**, select the VPN interfaces, and apply.
The panel adds a Block rule for the selected interfaces with the **Private networks** destinations.
The set covers private, shared and link-local IPv4 ranges, IPv6 local ranges and loopback addresses.
Existing rules keep their order and take precedence by default. To allow a particular internal service or
resolver, place a narrower rule before the Block rule.

The panel resolver still receives TCP and UDP DNS on port 53 on interfaces where DNS redirection is enabled.
The preset preserves the DNS configuration of all existing rules.
On interfaces with DNS redirection disabled, an internal resolver needs an explicit permit rule.

Applying the preset again adds only interfaces without a corresponding rule. The destinations can be edited and the rules can be moved, disabled or deleted like other routing rules. Installs and updates do not
activate the preset automatically.

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
| `POST /api/routing-entries/profiles/internet-only` | `routing:write` |

The new editor submits a `criterion` with `targets`, `dnsMode`, `dns` and optional `dnsMembers` to the same
routing entry endpoints. Older requests that use ordered `ruleIds` and existing `/routing/addresses` links remain
supported. The first row produced from a migrated rule keeps its ID and edit link.

## Destinations

`geoip:ru` selects a country and `geosite:youtube` a category. `203.0.113.0/24` and `8.8.8.8` are address ranges;
`example.org` and `domain:example.org` are domains; `cidr:10.0.0.0/8` explicitly names a range. `keyword:ads` matches
names containing that word. Keywords and domain categories obtain their addresses from the resolver or from names
read from connections; they do not supply static address ranges. An address set takes up to 1024 targets.

Use **Any destination** together with a client, interface, port or protocol filter for rules with no address
restriction. The pinned **Default** row is the unconditional policy. An empty catch-all carried over from an
earlier release keeps its existing meaning.

## DNS of a rule

**Default** is a live reference to the selected processing method:

- a linked server supplies its saved DNS, including DNS imported from its client configuration;
- a balancer supplies its own DNS when configured, otherwise the DNS of the member selected for the question;
- direct processing uses the host DNS reported by `GET /api/traffic-rules/dns-defaults`;
- blocking does not forward the question.

Changing a linked server or balancer updates this inherited choice without copying its addresses into every rule.
Missing DNS or an unavailable processing object does not select a different public resolver or a direct path.
Priority balancing shows the first available member. Round and sticky balancing can use different members, so the
panel shows possible member DNS values rather than claiming that one server is always selected.

Cloudflare, Google and Quad9 presets store explicit DNS addresses as `dnsMode: "custom"`; custom mode also accepts
the supported plain DNS, DNS-over-TLS and DNS-over-HTTPS forms described in [dns.md](dns.md). The address syntax
determines the resolver transport: a plain IP preset does not itself enable TLS or HTTPS. Choosing a preset replaces
previous member overrides. `dnsMode: "default"` uses the processing object and ignores explicit DNS and member maps.

Migrated custom policies may contain `dnsMembers`, resolver addresses for particular balancer members. These remain
visible when custom DNS is selected. On an old multi-set entry, the first matching set in its saved order supplies DNS.

## Ordered rules

| Setting | Holds |
|---|---|
| Name | what the rule is shown under, up to 32 characters |
| Processing | a linked server, a balancer, direct access, or blocking |
| Destinations | addresses and domains, or any destination when other filters narrow the rule |
| Clients | client names; empty selects every client |
| Interfaces | incoming interface names; empty selects every interface |
| Destination ports | single ports and inclusive ranges such as `443` or `1000-2000` |
| Source ports | the ports the traffic comes from, in the same form |
| Protocol | `any`, `tcp` or `udp` |
| Enabled | whether the rule participates in routing |

Matching any listed target satisfies the destination condition. Client, incoming-interface, protocol and port
filters are additional conditions on the rule. Up to 64 conditions of each filter kind are accepted. Renaming a client or interface updates
the rules naming it. Removing the last named client or interface disables the affected rule instead of broadening
it to everyone; removing one of several names retains the remaining filter.

Rules are evaluated from top to bottom and the first match decides. `move` with `{"up": true}` or `{"up": false}`
swaps a rule with its neighbour. `{"to": 3}` places it third, counting from one, and renumbers the others; a position
beyond the end places it immediately before the pinned Default row. Dragging is available on an unfiltered table, and arrow buttons also change priority.
Disabled rules do not participate. Unmatched traffic uses the Default row's selected route.

DNS chooses the first rule matching the queried name, client and incoming interface. Source ports, destination
ports and transport protocol filter application traffic, but are ignored for DNS selection: a DNS question does
not contain the port or protocol of a future connection. The DNS question travels through the processing object
selected by that rule. A rule with DNS set to **Next rule** yields only DNS selection. This lets a port-specific block
drop matching packets while domains still resolve.

## What the host takes

The compiled rules are loaded together into `inet amneziageo_rt` with `nft -f -`. Each ordered rule has IPv4 and IPv6
interval sets `r<id>v4` and `r<id>v6`, plus timeout name sets `n<id>v4` and `n<id>v6`. The sets contain the rule's destinations and learned DNS answers. Names and cached addresses are retained across ruleset replacement; see
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
The panel opens the tester from the Routing tab or at `/routing/test`. The resolver's interception and DNS guards remain
ahead of ordinary forwarding rules, as described in [dns.md](dns.md).

## Migration and existing configurations

The row upgrade splits a previous entry's ordered criteria into adjacent rows. The first retains its original
ID. New rows inherit the original action, filters and enabled state and retain each criterion's DNS, including
explicit member overrides. Editing the destinations or DNS of a shared migrated criterion through the row editor
copies it for that row. The migration adopts an eligible final resolver rule as the Default row or adds a new
direct fallback. It leaves an earlier, disabled or scoped rule in its existing relative position.

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
method. The rule editor shows the resulting effective DNS.
After this schema migration, downgrading the application also requires restoring the matching database backup
created before the upgrade. An older binary must not be started against the migrated database alone.

## Taking routing to another server

| Route | Right |
|---|---|
| `GET /api/routing/export` | `state:read` |
| `POST /api/routing/import`, `POST /api/routing/import/apply` | `routing:write` |

Export format 4 contains address definitions in `rules` (`name`, `targets`, `dnsMode`, `dns`, `dnsMembers`), ordered
processing rules in `entries` (including address-set names in `rules`, filters, processing and the optional `isDefault` marker), routing presets and
geo-source addresses. Linked servers, balancers and DNS service listener/cache settings remain specific to each
server. Imported rules with missing processing objects retain their references and cannot use those objects until
they are registered.

Import preview reports definitions and processing rules that will be added or replaced, plus unresolved references.
Applying the same file updates matching names and leaves objects absent from the file unchanged. Earlier formats
1–3 remain importable through conversion to the current model; they are not exported again as obsolete objects.
The utility provides `amneziageo-server routing export <file>` and `routing import <file>`, with `--apply` to apply.
