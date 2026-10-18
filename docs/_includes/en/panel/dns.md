# {{ page.title }}

The resolver of the panel answers the questions of the tunnel clients about names itself: it passes a question to an upstream server, over HTTPS unless told otherwise, gives the answer to the client and puts the addresses it sees into the sets of the rules that compare by this name. This is how a rule by domain works: the kernel decides by address, and the resolver brings the addresses. Along the way the questions of the clients do not leave the server as plain DNS.

A fresh panel keeps the resolver off: the clients get name servers in their configuration and ask them through the tunnel. It is turned on by the question of the installation, item 29 of the console menu `DNS for Clients`, the command `amneziageo-server dns on`, or "Answer the clients" here. A resolver that is off does not run, holds no port and keeps no connections to upstream servers.

## State

Above the settings the page shows what the resolver does: "Running" or "Stopped", "Listening on" with the addresses it took, "Questions", "From cache", "Unanswered", "Addresses in the sets", "Waiting" and "Rules by name". A failure stands in the same line: no upstream server answers (`no name server answers: ...`), the channel is off, there is no address to answer on (`the resolver does not run yet: there is no address to answer the clients on`, while there is no interface at all).

## Resolver

| Field | What it sets |
|---|---|
| Answer the clients | whether the resolver runs |
| Port | the port of the questions, 53 by default; a port below 1024 needs `CAP_NET_BIND_SERVICE` |
| Listen addresses | the addresses on which questions are taken; empty means the addresses of the interfaces of the panel. An address the host does not have is skipped |
| Upstream servers | the name servers the questions are passed to, and how each is asked, see below |
| Ask through the channel | the channel or group through which the questions leave; "Through the host" is the way out of the host itself |

An upstream server is named by address, not by name, so that nothing has to be resolved beforehand:

| Entry | How it is asked |
|---|---|
| `https://1.1.1.1/dns-query`, `https://9.9.9.9:5053/dns-query` | DNS over HTTPS, port 443 and the path `/dns-query` unless others are written |
| `tls://1.1.1.1`, `tls://1.1.1.1:853` | DNS over TLS, port 853 |
| `1.1.1.1`, `8.8.8.8:5300`, `[2606:4700:4700::1111]:53` | plain DNS: UDP, and TCP for a client that came over TCP |

A fresh panel asks `https://1.1.1.1/dns-query` and `https://8.8.4.4/dns-query`; `amneziageo-server dns set --upstreams default` brings them back. The certificate of the server is checked by its address under the trusted roots of the host; Cloudflare, Google and Quad9 carry their addresses in their certificates. The servers are asked in order; one that did not answer goes to the end for thirty seconds and is asked on the side, so that the clients do not wait for it. Nothing falls back to plain DNS by itself: it is asked only where it is written. When three questions in a row found no server, the failure goes into the state and the log, and the services count the resolver as down until a server answers.

"Ask through the channel" puts the mark of the channel on the questions, so that the answers come from where the traffic will go; without it, where the local network answers about a service differently from the network behind the channel, a rule by name fills up with foreign addresses. The channel is picked for each question: a "Priority" group gives the questions to its first live member, "Round" hands them out in turn, "By client" behaves like "Priority", because all the questions come from the host. While the chosen channel is off or carries nothing, the resolver does not ask and answers the clients `SERVFAIL`, and the state names the reason; with a channel chosen, the questions never leave through the host. The entries of client templates and the names in the route test are resolved the same way.

## Cache

| Field | What it sets |
|---|---|
| Record TTL, min | for how many minutes an answered address stands in the set of a rule |
| Cache size, entries | how many answers are kept in memory, 0 means none |
| Answer TTL: min, s and max, s | the bounds the lifetime of an answer from the cache is clamped to |

## Clients

| Field | What it sets |
|---|---|
| Take the questions of the clients | whether to turn to the resolver the questions a client sent to another name server on port 53 |
| Stop DNS over TLS | drop port 853 for the clients while at least one rule compares by name |
| Stop DNS over HTTPS | drop port 443 to known public name servers while at least one rule compares by name |

The resolver does not see a question inside TLS or HTTPS, so the two stops keep the clients on the resolver; they go on the host only while some rule or basic list compares by name. Another AmneziaGeo server that leaves through this one and asks its servers over HTTPS falls under the stop too: the nearer panel then names its upstream server as plain DNS (`1.1.1.1`) and asks through the channel, while the farther one takes the question with its resolver and asks out over HTTPS.

## Saving

"Save" writes the settings, and the resolver and the rules take them with the "Restart" button in the header, which appears while saved settings wait; it restarts the resolver, or the whole panel if its settings wait too. "Cancel" drops what is not saved. A change of the interfaces restarts the resolver on their addresses with the settings it runs with.

## How it works

While the resolver runs on port 53 and answers at the addresses of the interfaces, the `DNS` line of the file of each client carries the address of its interface instead of public servers, and so do the subscription links; a template with name servers of its own overrides this, a template without them takes the resolver. The resolver answers only through the tunnel, so its address also goes into `AllowedIPs` of the client when the ranges of the template or the interface leave it out. Where the panel keeps the ports of an interface open in the firewall, it lets the clients of the interface to the resolver too: the port of the resolver is opened on the interface for UDP and TCP, see [Ports in the firewall](interfaces.html#ports-in-the-firewall).

An interface whose ports the panel does not hold is left to the host: its clients get no answer until the port of the resolver is let in on its interface. The panel reads ufw and names the interfaces whose clients it keeps from the resolver: the DNS page writes this under "Answer the clients" ("The server firewall (ufw) keeps the clients of <interfaces> from the panel resolver (port 53)"), and so does the form of a template with [Route by name](templates.html#route-by-name), with the commands that let them in. On the host, sub-item 13 of menu item 23 `Let the clients reach the resolver` and the command `amneziageo-server dns open` give ufw `allow in on <interface> to any port 53` for each such interface, with the comment `amneziageo resolver <interface>`; item 29 and the installation offer this themselves once the resolver answers.{% if page.kind == "docker" %} From the container the sub-item prints the commands for the host.{% endif %} A host without ufw keeps nobody out.

The resolver also asks the names of the rules itself, without waiting for a client: every name a rule compares by, the names of a `geosite:` category included, is asked, and the answers go into the set of the rule, so the rule works from the moment it is saved, even for a service whose address the client already holds in its cache. The names are asked in batches of sixteen, no more often than twice a second and no more than 256 per rule, and are asked again when half of the "Record TTL" has passed; a name without an answer is repeated after two seconds, and the wait doubles up to a minute. Keywords and expressions of categories are not asked: they have no name.

An answer that brought an address not yet in the sets goes to the client once the address is in the set, at most half a second later, so the first connection to it already follows the rule. A rule keeps its 128 newest addresses standing: once a minute they are written to the database and after a restart come back into the set before the first question, so a rebooted machine does not leave the rule empty.

An answer larger than the datagram of the client goes out truncated with a flag, and the client asks again over TCP. On the host the resolver adds the `resolve` chain, which turns the questions from the interfaces of the panel to the resolver, and the lines with the stops of 853 and of 443 towards the sets `doh4` and `doh6` of public servers.

## Stand-in addresses

The clients of a template with [Route by name](templates.html#route-by-name) on are answered by the resolver about the names of the template with stand-in addresses out of `198.19.0.0/16`. The resolver asks the name as it does for any client, so the rules take the real addresses into their sets and the connection log knows the name. The client gets one `A` record with the stand-in address and a lifetime of at most 300 seconds; `AAAA` and `HTTPS` come back empty. A name that leads to a name of the template through an alias takes a stand-in too.

The host holds the map `stand` in the table `amneziageo_rt`: a new connection of a client to a stand-in goes to the real address (`dnat`), the rules decide it by the real address in a chain right after that, and a stand-in the map does not hold is refused with a reset. The panel writes the names with their stand-ins to its database ten seconds after they change and when it stops, and lays them again when it starts; a name nobody asked about for seven days lets its address go.

## Names from connections

A client that asks its own name server, or any client with the resolver off, goes to an address no answer of the panel carried. So the panel can read the name from the connection itself while at least one rule compares by name: the server name from the first TLS message on TCP 443, the `Host` header on TCP 80 and the server name from the first QUIC packets on UDP 443. The address goes into the sets of the rules the name fell under, for the "Record TTL".

The reading is off until the environment of the panel carries `Names__IsEnabled=true`; with it the panel takes the first connection to an address to itself (TPROXY to port 7319), reads the name, decides by the rules like the route test, puts the address into the sets and leads the connection itself through the channel of the rule, through the host, or nowhere for a blocking rule; the next connections to the address are decided by the kernel. Only connections to ports 80 and 443 of public addresses are taken, no more than 300 a second and 4096 at once. The state of the resolver shows what of this works, and the connections the panel led itself go into the [connection log](diagnostics.html#connection-log). Details are in the reference of the repository, [dns.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/dns.md#names-read-out-of-the-connections).

## Refusals

| Message | What stands behind it |
|---|---|
| No upstream name server is named; The address of the upstream server was not read | "Upstream servers" |
| The address to listen on was not read; The port is outside 1 to 65535 | the addresses and the port |
| The record TTL is outside the range; The cache size is outside the range; The answer TTL is outside the range | the cache |
| There is no such channel | "Ask through the channel" |
