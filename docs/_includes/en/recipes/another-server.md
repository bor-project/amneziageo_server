# {{ page.title }}

The task: the clients connect to this server, and the sites from a list open through another server, for example abroad. This server becomes a client of that one and sends into the tunnel only what fits the rule; the rest goes through the host as before.

## 1. A client configuration on the other server

The other server needs a client configuration for this host. On an AmneziaGeo server it is "Clients" > "Add", a name like `relay-home`, and the `.conf` file from the "Export" tab. Any AmneziaWG or WireGuard server that gives out a client file will do.

## 2. Channel

"Routing" > "Channels" > "Add":

1. Type "AmneziaWG".
2. At the bottom of the form paste the client file into the "Client configuration" field and press "Read": the keys, the interface address, the server with its port and the obfuscation are filled from it.
3. "Name": up to 15 characters, for example `awg-de`.
4. "Reachability check": "Name server" `1.1.1.1`, "Every, seconds" 30. The question goes through the tunnel, and an answer means the tunnel is alive.
5. Leave "Close private networks" on: the clients will not get into the network behind that server.
6. "Save".

In a few seconds the channel in the list shows "ready", the handshake and traffic. "refused" with a reason means the host did not raise the interface; "no handshake yet" means the server did not answer: check the address, the port and the obfuscation, which must match the server exactly.

If the network to that server lets only web traffic through, use the "WebSocket" type and the "Websocket proxy" field: for an AmneziaGeo server it is `wss://<its host>:<services port>/<WebSocket path of the interface>`, and the path is seen in the form of its interface.

## 3. Resolver

A rule by domains works through the resolver of the panel: "Routing" > "DNS", "Answer the clients" on, "Ask through the channel" set to `awg-de`, so that the addresses come from where the traffic will go, then "Save" and "Restart" in the header. If the resolver has not been turned on yet, item 29 of the console menu turns it on as well.

## 4. Rule

"Routing" > "Rules" > "Add":

1. "Name" `abroad`, "Action" "Through a channel", "Channel" `awg-de`.
2. "Where to": the keys and domains that must go through that server, for example `geosite:youtube`, `geoip:ag-youtube`, `geosite:netflix`, `example.com`. What a key carries is shown by "Show what it holds" in the templates and told on [Geo lists](../panel/geo.html).
3. Leave "Hold the traffic while the channel is down" on: while the tunnel to that server is down, the traffic of the rule is dropped rather than leaving through the host under the address of this server.
4. "Enabled", "Save".

In the list the rule shows "on the host" and the number of ranges and names. A rule stands in the order of reading: if a rule above takes the same traffic, move it with the arrows.

## 5. Check

"Routing" > "Test": "Name or address" `youtube.com`, "Port" 443, "Client" any client of the panel, "Test". The outcome is "Through awg-de" and the rule `abroad` with "matched" by name or range. From the device of a client open a site from the list and any other one: the first sees the address of that server, the second the address of this one.

## If it does not work

- The rule shows "traffic held": the channel carries nothing, see its status. While the reachability check does not get through, the groups and the rules count the channel as dead.
- The site opens through the host: a rule higher in the list took the traffic first, or the name did not get into the sets. "Test" names the rule and what it matched by; "Addresses in the sets" on the DNS page grows with the answers of the resolver.
- Another AmneziaGeo server with its own rules by name cuts off DNS over HTTPS from this one: name the upstream server here as plain DNS (`1.1.1.1`) and ask through the channel, and that server takes the question with its resolver, see [DNS](../panel/dns.html#clients).
