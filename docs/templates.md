# Templates

The panel holds three kinds of template, one table each: client templates, interface templates and proxy
templates. A template carries the values that are not the own of one instance, and an instance points at it
instead of keeping a copy: changing a template changes every instance that takes it, and the subscription of a
client hands out the new file at once, because the file is built when it is asked for.

A fresh database starts with one template of each kind: the client template `default`, the interface template
`amnezia-3.1` and the proxy template `websocket`. A template no instance takes can be removed, a template an
instance takes is refused with `template-in-use` (409). An install that already carries interfaces or proxies
gets a template for the values each of them holds, so nothing changes under a running tunnel; interfaces that
hold the same values share one template, named after the first of them.

The panel holds the three kinds under `Connections`, `Templates`, one subsection each. A list names the
template and how many instances take it; the name and the menu of the row lead into the settings, where the
template is changed and removed. Saving an interface template answers with every interface it was written into
and whether it came up, and the form keeps the page when one of them did not.

The form of an instance carries what belongs to the instance and the choice of its template, and the values of
the template are shown next to the choice, without being editable. An instance left without a template keeps
its own values, and the form opens them for editing, which is how an imported configuration is kept as it came.

## Client templates

A template names what the file of a client takes in place of the settings of its interface: the ranges the
client routes into the tunnel (`AllowedIPs`), the name servers, the packet size and the keepalive. A field the
template leaves empty takes the default of the panel: `0.0.0.0/0`, and `::/0` too for a client with an IPv6
address, the name servers `1.1.1.1` and `1.0.0.1`, the packet size 1420 and the keepalive 25. A client without a
template keeps the settings of its interface. The keys, the address of the client, the endpoint and the
obfuscation always come from the interface: they have to match the server.

## Where the ranges come from

The ranges are not written by hand. The template keeps a list of entries, the way the client keeps its lists:

| Entry | Written as | Gives |
|---|---|---|
| GeoIP | `geoip:ru` | the ranges the geo databases carry for the country |
| GeoSite | `geosite:youtube` | the addresses the names of the category resolve to |
| Network | `cidr:10.0.0.0/8` | itself |
| Address | `cidr:1.2.3.4` | itself |
| Domain | `domain:example.com` | the addresses the name resolves to |

A network, an address or a name written bare takes its prefix as it is added, and so does a link, which leaves
its host. A name is asked for both families through the name servers of the panel resolver (`upstreams` in
[dns.md](dns.md)); of a geosite category only the domains and the exact names are asked, keywords and
expressions have no address of their own. One pass asks at most 4000 names and stops after a minute; a name that
did not answer in time counts as not found.

What the entries give is folded into the fewest ranges that cover it and kept with the template, together with
the entries that gave nothing and the time of the pass. This is what goes into `AllowedIPs` of every client of
the template; when nothing came out, the default ranges go instead.

The list is worked out again when the template is saved and by `Refresh` (`POST /api/templates/{id}/refresh`):
names move and geo databases update, and the files of the clients follow only after a refresh.

A template a client takes cannot be removed (`template-in-use`): pick another template for those clients
first.

| Setting | Holds |
|---|---|
| Name | up to 64 characters, one of a kind |
| Entries | up to 256 geo keys, networks, addresses and domains; empty gives `0.0.0.0/0`, with `::/0` for a client with an IPv6 address |
| DNS | name server addresses; empty gives the resolver of the panel, or `1.1.1.1` and `1.0.0.1` when it is off |
| MTU | 576 to 9000; empty gives 1420 |
| Keepalive | 0 to 65535 seconds, 0 turns it off; empty gives 25 |

`GET /api/templates` lists the templates with their entries, the ranges they gave, the entries that gave
nothing, the time of the last pass and the number of clients that take each; `GET /api/templates/{id}` returns
one; `GET /api/templates/defaults` returns what an empty field gives, `::/0` among the ranges when an interface
carries IPv6; all three need `state:read`. `POST /api/templates`, `PUT /api/templates/{id}`,
`POST /api/templates/{id}/refresh`, `POST /api/templates/preview` and `DELETE /api/templates/{id}` need
`clients:write`. A request writes
`entries`, never `allowedIps`. A client names its template in `templateId`; a number the panel does not hold is
refused with `unknown-template`.

`POST /api/templates/preview` tells what a list of entries gives before a template is kept: how many ranges
came out, the first 1000 of them, the entries that gave nothing, and per entry how many ranges it gave and the
first 200. The panel asks it while a template is edited, so an entry shows what it brings and what stays when
it goes: a range two entries share is held by the one that remains. It needs `clients:write`. The panel
keeps the count beside the field: over 12000 ranges it warns that the speed may drop, over 16000 it does not
let the template be saved.

`GET /api/geo/entries?key=geosite:youtube&limit=200` shows what a geo key carries: how many entries and the first
of them, the ranges of a country or the names of a category, a name written `full:`, `keyword:` or `regexp:` when
it is matched that way. It needs `state:read`.

The same routes answer under `/api/templates/clients`, which is where the panel asks for them.

## Interface templates

An interface template carries everything an interface does not hold of its own: the name servers, the ranges the
clients route into the tunnel, the packet size, the keepalive, the silence after which a device counts as gone,
the ranges the clients are kept out of, and the whole of the obfuscation. The port and the range it starts from
are in the template as well, but only as the values a fresh interface takes: the port and the address of an
interface are its own. The name of the interface, the address clients reach it at, the port, the range, what the
clients take from the tunnel, the masquerade, the open port and the keys stay with the interface.

The template also names the client template the clients of its interfaces take (`clientTemplateId`); left empty,
a client keeps the settings of its interface, as before.

| Setting | Holds |
|---|---|
| Name | up to 64 characters, one of a kind |
| ListenPort | 1 to 65535, the port a fresh interface starts from |
| Subnet | the range a fresh interface starts from |
| DNS, AllowedIPs, MTU, Keepalive, OfflineAfter, Blocked | as in [configs.md](configs.md) |
| Obfuscation | as in [configs.md](configs.md), the same values for every interface of the template |
| ClientTemplateId | a client template, or empty for the settings of the interface |

The built in template carries the values of a fresh interface: the port 51820, the range `10.8.0.1/24`, the
packet size 1420, the keepalive 25, the name servers `1.1.1.1` and `1.0.0.1`, the private ranges kept away from
clients, the silence of 60 seconds, and AmneziaWG 3.1 obfuscation drawn when the database is made, so two
installs do not look alike. The junk packet count follows the range the README of amneziawg-go recommends, 4 to
12; the junk sizes are 50 to 1000 bytes and the handshake padding 15 to 149 bytes, which is over the 12 bytes
the header protection needs.

Changing an interface template writes its values into every interface that takes it and raises each of them
again; the answer carries the template and, per interface, whether it came up and what the host said. The keys
and the obfuscation have to match on both sides, so a client of such an interface keeps the tunnel only after it
has the new file: with a subscription it takes it by itself, without one the administrator hands it over.

`GET /api/templates/interfaces` and `GET /api/templates/interfaces/{id}` need `state:read`;
`GET /api/templates/interfaces/draft`, `POST /api/templates/interfaces`, `PUT /api/templates/interfaces/{id}`
and `DELETE /api/templates/interfaces/{id}` need `interfaces:write`. An interface names its template in
`templateId` of `POST /api/configs` and `PUT /api/configs/{id}`, and `GET /api/configs/draft?templateId=`
returns a draft of that template; without a number the draft takes the built in template. A number the panel
does not hold is refused with `unknown-template`.

## Proxy templates

A proxy template carries the way the proxy takes tunnels in, whether the port is held open in the firewall,
whether a path no one guesses is drawn for a websocket proxy, where a wireguard proxy passes the datagrams on,
and the addresses it takes. The port is in the template as the value a fresh proxy starts from, because two
proxies of one kind cannot listen on the same port. The name, the path and the certificate of a proxy are its
own.

| Setting | Holds |
|---|---|
| Name | up to 64 characters, one of a kind |
| Kind | `ws` or `wg` |
| Port | 1 to 65535, the port a fresh proxy starts from, 443 in the built in template |
| Opened | whether the panel holds the port open in the firewall |
| MakePath | whether a websocket proxy takes a path of its own |
| Target | where a wireguard proxy passes the datagrams on |
| Sources | the addresses and the networks the proxy takes, empty for any |

Changing a proxy template writes its values into every proxy that takes it and starts each of them again; the
answer carries the template and, per proxy, whether it runs and what the host said.

`GET /api/templates/proxies` and `GET /api/templates/proxies/{id}` need `state:read`;
`GET /api/templates/proxies/draft`, `POST /api/templates/proxies`, `PUT /api/templates/proxies/{id}` and
`DELETE /api/templates/proxies/{id}` need `routing:write`. A proxy names its template in `templateId` of
`POST /api/proxies` and `PUT /api/proxies/{id}`, and `GET /api/proxies/draft?templateId=` returns a draft of
that template, on the first free port from the one the template names.
