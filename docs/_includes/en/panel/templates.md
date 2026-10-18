# {{ page.title }}

A template carries what in a client file does not belong to one client: `AllowedIPs`, name servers, the packet size and keepalive, and also the routing presets the AmneziaGeo app gets. A client points at a template rather than holding a copy: editing the template changes the files of all its clients, and a subscription serves the new file at once, because the file is assembled at the moment of the request. A fresh panel starts with the `default` template.

{% include shot.html name="templates" alt="List of templates: a template with geo keys and the default one, the number of addresses in the file and of clients" caption="Templates of the server." %}

The "Templates" tab of the "Clients" page lists the templates with their `AllowedIPs`, DNS, MTU, keepalive and the number of clients; the name leads to the settings, "Add" opens a new one, "Duplicate" in the row menu makes a copy "<name> copy". The "Template" field in the settings of a client picks its template, "Default" leaves the client the settings of the interface.

## Main

"Name": up to 64 characters, one of a kind in the panel.

## Routing

The entries of a template are what the client sends into the tunnel. Ranges are not written by hand: the template holds a list of entries, like the lists of the AmneziaGeo client, and unfolds them into addresses itself.

| Entry | Written as | Gives |
|---|---|---|
| GeoIP | `geoip:ru` | the ranges of a country or a network from the geo databases |
| GeoSite | `geosite:youtube` | the addresses the domains of the category resolve to |
| Network | `cidr:10.0.0.0/8` | itself |
| Address | `cidr:203.0.113.10` | itself |
| Domain | `domain:example.com` | the addresses the name resolves to |

A network, address or name typed without a prefix gets one as it is added; a link keeps its host. The names are asked in both families through the upstream servers of the resolver of the panel; from a geosite category only domains and exact names are asked, since keywords and expressions have no address. One pass asks up to 4000 names and ends after a minute; a name without an answer counts as not found. Addresses a packet through the tunnel would not go to (loopback, zero, link-local, multicast, broadcast) are dropped; a private address stays, because a name of an internal resolver may lead to a host behind the server.

What the entries gave is folded into the fewest ranges and kept with the template, together with the entries that gave nothing and the time of the pass: "Refreshed <time>", "Nothing found for: <list>". This is what goes into `AllowedIPs` of every client of the template; an empty list gives `0.0.0.0/0`, and a client with an IPv6 address also gets `::/0`. A name server of the file inside the network of the interface, the resolver of the panel among them, answers only through the tunnel: when the ranges leave it out, it is added to `AllowedIPs` as a single address. The list is computed again on save and with the "Refresh" button: names move and the geo databases update, and the files of the clients change only after a new pass.

While a template is being edited, the panel counts what each entry gives: "Addresses: N" on the entry and on the whole template, "Show what it holds" unfolds the first addresses or names of a category ("No entries: the category is absent from the downloaded databases"). Over 12000 ranges means "the speed may drop", over 16000 "too many addresses to save". A template holds no more than 256 entries.

"Routing presets" are the lists the AmneziaGeo app of a client of the template gets, in the order of handing out, while routing happens on the client, see [Routing presets](presets.html); "No presets: add them in the tab Routing presets" while none are made.

## Route by name

A client with nothing but `AllowedIPs` cannot follow a name. A service behind a CDN answers with the cache nearest to whoever asks; the caches stand in the networks of providers and change, and neither the addresses a pass found nor the geo ranges of the service cover them. With the "Route by name" switch in the "Routing" part, the names of the template (`geosite:` and `domain:`) are not unfolded into addresses, and such entries show "by name" instead of a number of addresses. The resolver of the panel answers a client of the template about these names with a stand-in address out of `198.19.0.0/16`: one address per name, the same for as long as the name is asked about, and the same on every server as long as two names do not land on one address. The host leads a connection to the stand-in to the real address the resolver got for the name and decides it by the real one, so the rules of the server see such a connection as any other, see [Stand-in addresses](dns.html#stand-in-addresses).

{% include shot.html name="template-form" alt="Form of a template: geosite entries marked by name and the Route by name switch on" caption="A template with routing by name." %}

The file of such a client carries the resolver of the panel as its only name server (the "DNS" field of the "Network" part shows its addresses and cannot be edited), and in `AllowedIPs` the networks and addresses of the template, `198.19.0.0/16` and the address of the resolver. A geo key of addresses (`geoip:`) stays a range: it serves the applications that go to addresses without asking a name, Telegram and calls among them.

The switch works while the resolver of the panel answers the clients on port 53 and the firewall of the host lets them reach it. The template is saved with the switch on in any case, and the form under it names what keeps it from working, with the command that mends it:

| Message | What to do |
|---|---|
| Will not work: the panel resolver is off | turn the resolver on on the [DNS](dns.html) page, with `sudo amneziageo-server dns on` or with menu item 29 |
| Will not work: the panel resolver answers on port N, while the clients ask on 53 | set port 53 on the DNS page |
| Will not work: the server firewall (ufw) keeps the clients of <interfaces> from the panel resolver (port 53) | on the server `sudo amneziageo-server dns open`, menu item 23, sub-item 13, or `sudo ufw allow in on <interface> to any port 53` for each interface |

An application with name servers of its own goes past the stand-ins; the resolver keeps the clients off the known public servers with "Stop DNS over HTTPS" and "Stop DNS over TLS". A name with a stand-in gets no IPv6 answer and no `HTTPS` record, so the client goes to it over IPv4. A stand-in that leads nowhere, after a client came over from another server or for a name nobody asked about for a week, is refused at once, and the client asks for the name again.

## Network

| Field | What it sets |
|---|---|
| DNS | the name servers of the client; empty means the resolver of the panel when it answers inside the tunnel, otherwise `1.1.1.1` and `1.0.0.1`; with routing by name, the addresses of the resolver of the panel, and the field cannot be edited |
| MTU | 576 to 9000; empty means 1420 |
| Keepalive | 0 to 65535 seconds, 0 turns it off; empty means 25 |

The keys, the address of the client, the address of the interface and the obfuscation always come from the interface: they must fit the server.

## Removing

A template taken by at least one client is not removed: "The template is in use". First the clients get another template.

## Refusals

| Message | What stands behind it |
|---|---|
| The name of the template is empty or longer than 64 characters | the name |
| Not a geo key, a network, an address or a domain; A template carries at most 256 entries | the entries |
| The name server is not an address; MTU is outside 576 to 9000; Keepalive is outside 0 to 65535 | network |
| There is no such routing preset | the template names a preset that does not exist |
