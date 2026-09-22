# Templates

A client template carries the values of a client file that are not the own of one client, and a client points
at it instead of keeping a copy: changing a template changes every client that takes it, and the subscription of
a client hands out the new file at once, because the file is built when it is asked for.

A fresh database starts with the client template `default`. A template no client takes can be removed, a
template a client takes is refused with `template-in-use` (409).

The panel holds the templates under `Connections`, `Templates`. The list names the template and how many clients
take it; the name and the menu of the row lead into the settings, where the template is changed and removed.
The form of a client carries the choice of its template and a link to it.

## What a template names

A template names what the file of a client takes in place of the settings of its interface: the ranges the
client routes into the tunnel (`AllowedIPs`), the name servers, the packet size and the keepalive. A field the
template leaves empty takes the default of the panel: `0.0.0.0/0`, and `::/0` too for a client with an IPv6
address, the name servers `1.1.1.1` and `1.0.0.1`, the packet size 1420 and the keepalive 25. A client without a
template keeps the settings of its interface. The keys, the address of the client, the endpoint and the
obfuscation always come from the interface: they have to match the server.

A template also says whether a client of AmneziaGeo may route by lists of its own, on by default. A client
takes the word of its template or says `on` or `off` itself, and learns the outcome from the hello of its
endpoint, see [services.md](services.md).

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
