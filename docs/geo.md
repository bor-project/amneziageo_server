# The geo databases

`Geo` holds the rule databases the routing is matched against: the v2ray `geoip.dat` and `geosite.dat`
files, the same ones the client reads. A source is an address the server downloads from, a kind, and a
place in the order. The panel keeps the sources in SQLite and the files themselves in a directory of
their own.

## Rights

| Route | Right |
|---|---|
| `GET /api/geo/sources` | `state:read` |
| `GET /api/geo/keys` | `state:read` |
| `POST /api/geo/sources` | `routing:write` |
| `PUT /api/geo/sources/{id}` | `routing:write` |
| `DELETE /api/geo/sources/{id}` | `routing:write` |
| `POST /api/geo/sources/{id}/move` | `routing:write` |
| `POST /api/geo/sources/{id}/update` | `routing:write` |
| `POST /api/geo/update` | `routing:write` |

`routing:write` is a right of its own, next to `interfaces:write`, and the built in `admin` role carries
it. It counts as sensitive: a caller without a fresh login is refused.

## The settings of a source

| Setting | Holds |
|---|---|
| Name | the name the file is stored under, lower case, up to 32 characters |
| Kind | `geoip` for address ranges, `geosite` for domains |
| Address | the http or https link the file is downloaded from |
| On | whether rules are matched against the source |

The panel also shows what the last download left: the number of countries or categories, the size, the
time, and the reason when it did not go through.

## The order

Sources are read in the order they are held in, and a later source overrides an earlier one per entry:
if `geoip` and `geoip-ru-only` both carry `ru`, the ranges come from the one further down. `move` swaps
a source with its neighbour. A source that is off is skipped by every query.

## What a fresh install carries

| Name | Kind | Address |
|---|---|---|
| `zkeenip` | addresses | jameszeroX `zkeen-ip`, `zkeenip.dat` |
| `geosite` | domains | Loyalsoldier `v2ray-rules-dat`, `geosite.dat` |
| `geoip` | addresses | Loyalsoldier `v2ray-rules-dat`, `geoip.dat` |
| `geosite-ru-only` | domains | runetfreedom `russia-blocked-geosite` |
| `geoip-ru-only` | addresses | runetfreedom `russia-blocked-geoip` |

They are added on the first start of a fresh database and are removed and changed like any other source.
No file ships with the server: the five are downloaded on the first update. A source that joins the table
later reaches a panel seeded before on its first start after the update, once: it goes in front of the first
standard source that follows it here. A standard source removed by hand is not brought back, and one already
held under its name or address is not added twice. The set a panel has been given is kept in `Seeds`.

Voice and video calls of Discord go to the bare addresses of its voice servers, which the Discord app gets
without DNS, so `geosite:discord` misses them: `geoip:discord` from `zkeenip` carries them. `zkeenip` stands
first because it also carries `ru`, `google`, `cloudflare`, `telegram` and `fastly`, and those stay with the
databases below it.

## The download

`POST /api/geo/sources/{id}/update` downloads one source, `POST /api/geo/update` goes over every source
that is on. The request carries `If-None-Match` and `If-Modified-Since` from what was downloaded before,
so a database that did not change costs one answer and no traffic. The file is read before it is stored:
an address that answers with a page instead of a database is refused and nothing is overwritten. A file
larger than 256 megabytes is refused as well. What was downloaded lands under the name of the source in
the geo directory, through a temporary file, so a broken download leaves the previous one in place.

Downloads also run on their own: two minutes after the start, then every half hour, taking the sources
that are older than `Geo:UpdateHours` (24 by default). Setting it to zero leaves downloading to the
panel.

## Where the files are

The directory is `Geo:Path` from the configuration, the `AMNEZIAGEO_GEO` variable, or `geo` next to the
database file, in that order. One file per source, named after it, with the `.dat` suffix.

## What is read out of them

`GET /api/geo/keys` answers with every country code and category code across the sources that are on.
The standard databases give about 260 countries and 1500 categories, `zkeenip` adds some 25 networks such
as `discord`, `hetzner` or `youtube`, and the answer costs
milliseconds: the codes are taken from the head of each entry, the bodies are stepped over.

A rule names a key as `geoip:ru` or `geosite:youtube`, a domain, or a range. `GeoMaterializer` turns a
set of rules into both facets at once: the ranges the databases carry and the names, together with the
domain suffixes a country owns - `ru` also owns `su`, `xn--p1ai` and `рф`. `DomainMatcher` answers
whether one name falls under a set of entries, taking the four shapes a geosite entry comes in: exact,
suffix, keyword and expression. An expression is given 50 milliseconds and is dropped when it runs longer.
