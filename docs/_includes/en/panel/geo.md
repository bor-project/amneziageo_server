# {{ page.title }}

Geo lists are the geo databases rules and templates compare against: `geoip.dat` files with the ranges of countries and networks and `geosite.dat` files with the domains of categories, in the v2ray and Xray format, the same the AmneziaGeo client reads. A source is the address the panel downloads the file from, its type and its place in the order. The files lie in `geo` next to the database of the panel (`Geo__Path` sets the directory).

## List

| Column | What it shows |
|---|---|
| Name | the name the file lies under on the disk, lower case, up to 32 characters |
| Type | "Addresses" (`geoip`) or "Domains" (`geosite`) |
| Address | an http or https link to a `.dat` file |
| Entries | "Countries N" or "Categories N" after a download |
| Size, Updated | the size of the file and the time of the download; "not downloaded", "off", or the reason the download failed |
| Enabled | whether rules are compared against the source |

{% include shot.html name="geo" alt="Geo lists: the number of countries and categories and the sources with address, entries, size and time of update" caption="The sources of a fresh panel, downloaded." %}

"Update" on a source downloads it, "Update all" all the enabled ones. Sources are moved with the "Up" and "Down" arrows.

## Order

Sources are read in their order, and an entry from a source lower down overrides the same entry from a source above: if `geoip` and `geoip-ru-only` both carry `ru`, the ranges come from the lower one. A source that is off is skipped.

## What a fresh panel has

| Name | Type | From |
|---|---|---|
| `zkeenip` | addresses | jameszeroX `zkeen-ip`, `zkeenip.dat` |
| `geosite` | domains | Loyalsoldier `v2ray-rules-dat`, `geosite.dat` |
| `geoip` | addresses | Loyalsoldier `v2ray-rules-dat`, `geoip.dat` |
| `geosite-ru-only` | domains | runetfreedom `russia-blocked-geosite` |
| `geoip-ru-only` | addresses | runetfreedom `russia-blocked-geoip` |
| `amneziageo` | addresses | bor-project `amneziageo-geo`, `geoip.dat` |
| `amneziageo-geosite` | domains | bor-project `amneziageo-geo`, `geosite.dat` |

They are added at the first start on a fresh database and are changed and removed like any others; no files come with the panel, and all seven are downloaded at the first update. A source that appeared in this list later reaches the panel at the first start after an update, once; one removed by hand does not come back.

`zkeenip` stands first because besides `discord` it carries `ru`, `google`, `cloudflare`, `telegram` and `fastly`, and these keys stay with the databases below. Discord calls go to bare addresses of the voice servers, which the app gets without DNS, so `geosite:discord` does not catch them: `geoip:discord` from `zkeenip` carries them. The own databases `amneziageo` and `amneziageo-geosite` carry keys with the `ag-` prefix, so as not to override anyone else's: `geoip:ag-youtube` is what a YouTube client needs by address, together with the video caches, `geosite:ag-tiktok` and `geoip:ag-tiktok` are TikTok.

## Downloading

The request carries the `If-None-Match` and `If-Modified-Since` of the previous download, so a database that did not change costs one answer without traffic. The file is read before it is written: an address that answers with a page instead of a database is refused, and nothing is overwritten; so is a file over 256 MB. The download goes through a temporary file, so a broken one leaves the previous file in place.

Downloads also run by themselves: two minutes after the start and then every half hour for sources older than `Geo__UpdateHours` hours (24 by default; 0 leaves downloading to the button). Item 28 of the console menu and `amneziageo-server geo update` download everything now.

## What is read from them

A rule or a template names a key like `geoip:ru` or `geosite:youtube`. The usual databases give about 260 countries and 1500 categories, `zkeenip` adds some twenty networks such as `discord`, `hetzner`, `youtube`, and the own database the `ag-` keys. A country brings its domain zones as well: `ru` also `su`, `xn--p1ai` and `рф`. A geosite entry comes in four kinds: an exact name, a suffix, a keyword and an expression; an expression gets 50 milliseconds. What a key carries is shown by "Show what it holds" on an entry of a template, see [Templates](templates.html#routing).

The AmneziaGeo client app also gets the geo sources of the panel through hello, see [Routing presets](presets.html).

## Refusals

| Message | What stands behind it |
|---|---|
| The type is addresses or domains | the type |
| The address is not an http or https link to a .dat file; This address is already downloaded by another list | the address |
| The name is already taken; There is no such geo list | the name or the number |
