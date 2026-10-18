# {{ page.title }}

Routing decides where the traffic of the clients goes after the server: through a channel (a tunnel to another server), directly through the host, or into a block. The kernel decides, by nftables rules the panel assembles from its settings; the resolver of the panel turns domain names into addresses.

{% include children.html of="panel/routing" %}

## How it works

- **Rules** are read top down, and the first that fits applies. A rule compares the first packet of a new connection: where it goes (geo keys, domains, addresses), where it comes from (addresses, clients, interfaces), ports and protocol. A "Through a channel" rule puts the mark of the channel on the connection, and the connection keeps this path while it lives, even if the rules change later.
- **Basic lists** stand before the rules: "Block" drops, "Direct" lets out through the host without a mark.
- **Channels** are the ways out: the host itself, an AmneziaWG tunnel to another server or the same tunnel inside WebSocket; each channel carries its own mark and routing table. A group of channels under one name hands out the traffic by priority, round robin or per client, and a rule names a group the same way as a channel.
- **Geo lists** are the `geoip.dat` and `geosite.dat` databases from which rules and templates take the ranges of countries and the domains of categories.
- **DNS**: the resolver of the panel answers the clients for names, asks upstream servers over HTTPS and puts the addresses it sees into the sets of the rules that compare by name; without it a rule by domain works only by the addresses the resolver asked for itself.
- **Test** tells what the rules will do with a connection to a name or an address from a given client.

Traffic that fits no rule goes the way the host itself would send it. An answer that comes back through a channel carries no mark, and the host finds the client in its own tables. Rules need interfaces: without an interface the traffic comes to, there is nothing to mark.

Routing rules, channels, groups, geo sources and the resolver are changed by a role with the "Manage routing" permission; any role reads them. The rule set as the host holds it is printed by item 23 of the console menu, sub-item 12.
