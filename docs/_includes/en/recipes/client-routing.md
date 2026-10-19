# {{ page.title }}

The task: the clients get a list of sites that go through the VPN, the rest goes directly for them, and the server keeps this list. There are two ways: `AllowedIPs` in the client file, which any AmneziaWG app understands, and routing presets, which the AmneziaGeo app understands.

## Through AllowedIPs: a template

"Clients" > "Templates" > "Add": the name `split`, and under "Routing" entries like `geosite:youtube`, `geoip:ag-youtube`, `geosite:telegram`, `example.com`. The panel unfolds the entries into address ranges, and this goes into `AllowedIPs` of the file of every client of the template; "Addresses: N" under the entries says how many came out. "Save", then set "Template" to `split` for the clients that need it.

Any WireGuard client works this way: it sends into the tunnel only the addresses from the file. But the addresses of services change, while the file holds the ones of the last pass over the template: the panel computes it again once a day, after the geo databases change and with the "Refresh" button, and the client gets the new file through the subscription. Services behind a CDN with thousands of addresses give a long list: the panel warns about slowing down after 12000 ranges and does not save the template after 16000. More in [Templates](../panel/templates.html).

For such services a template has the "Route by name" switch: the names of the template are not unfolded into addresses, the resolver of the panel answers the clients about them with stand-in addresses out of `198.19.0.0/16`, and the server leads the connections to the real addresses, whichever cache answered. The file carries the resolver of the panel as its only name server, so this works with any AmneziaWG app; it needs the resolver of the panel answering on port 53 and the firewall of the server letting the clients reach it, and the form of the template tells by itself what keeps it from working. More in [Route by name](../panel/templates.html#route-by-name).

## Through presets: the AmneziaGeo app

The AmneziaGeo app watches the DNS answers itself and puts the addresses from its lists into the tunnel on the fly. The list for it is kept on the server as a preset.

1. "Clients" > "Routing presets" > "Add": the name `Blocked sites`, and under "Through the VPN" the same entries, `geosite:youtube`, `geoip:ag-youtube`, `geosite:telegram`; under "Block" you can put `geosite:category-ads-all`. "On by default", so that the app puts the list in use when it adds it. "All UDP through the tunnel" for calls and games that learn their addresses not through DNS. "Save".
2. "Clients" > "Templates" > the template of the clients > "Routing presets": check the preset, "Save". Leave the entries of the template under "Routing" empty: `AllowedIPs` stays `0.0.0.0/0`, and the app decides by the list what goes into the tunnel.
3. At the next hello the app adds the list and puts it in use. Together with the preset it gets the geo sources of the panel from the [Geo lists](../panel/geo.html) page, so it has the `ag-` keys of the own database too.

Editing the preset changes its time of change, and the card of the list in the app offers an update; the owner of the device accepts it or keeps their own. A list the owner removed comes back at the next hello. More in [Routing presets](../panel/presets.html).

## Which to choose

Presets are for the AmneziaGeo app: the list is live, updates by itself and does not swell the file. A template with entries is for any other client and for cases where routing must be built into the file. Both ways can be combined: a template with entries for other apps, a preset for our own, but then `AllowedIPs` limits the AmneziaGeo app too to what is in the file.

Routing on the server, the rules of the [Routing](../panel/routing.html) section, decides another thing: where the traffic goes after the server.
