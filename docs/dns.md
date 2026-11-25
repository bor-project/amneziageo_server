# The resolver

The panel answers DNS questions from tunnel clients and places resolved destination addresses into the sets used
by routing rules. Address sets in **Routing → Addresses** define the resolver policy. Ordered **Rules** select
the processing method and additional client/interface/port filters; see [rules.md](rules.md).

DNS selects the first rule matching the name, client and incoming interface. Application protocol and port filters
are ignored for DNS selection, while application traffic still uses every filter. Within that rule the first
matching address set supplies DNS. Questions travel through the rule's linked server, selected balancer member or
direct path. Blocking does not forward them. There is no independent Global/Outbound/Routing DNS-exit selector.

The resolver runs automatically while domain routing, stand-in addresses, clients choosing it or previously
exported client configurations require it. Its service page retains listener, port, cache and client-file settings.
The DNS choices of individual client configurations and templates remain separate from the server's resolver
selection for routed questions.

## Rights

| Route | Right |
|---|---|
| `GET /api/dns` | `state:read` |
| `GET /api/dns/conflicts` | `state:read` |
| `POST /api/dns/check` | `state:read` |
| `PUT /api/dns` | `routing:write` |
| `POST /api/dns/restart` | `routing:write` |

## The settings

| Setting | Holds |
|---|---|
| Mode | `Automatic` runs the resolver while the routing needs it, `Off` keeps it off |
| Port | the port questions are taken on, 53 by default |
| Listen on | the addresses questions are taken on, empty for the addresses of the configurations |
| An address lives | how long an answered address stays in the set of a rule, in minutes |
| Answers held in memory | how many answers are kept back, zero for none |
| An answer lives from, to | the bounds the lifetime of a kept answer is clamped to, in seconds |

A port under 1024 needs `CAP_NET_BIND_SERVICE`: under an ordinary account the resolver reports
`Permission denied` and takes no address. An address that is not on the host is skipped, and the
resolver answers on the rest.

## How the name servers are asked

A custom DNS entry of an address set names a resolver by address and determines its transport:

| Entry | Asked |
|---|---|
| `https://1.1.1.1/dns-query`, `https://9.9.9.9:5053/dns-query` | DNS over HTTPS: a `POST` of `application/dns-message`, port 443 and the path `/dns-query` unless others are written |
| `tls://1.1.1.1`, `tls://1.1.1.1:853` | DNS over TLS, port 853 unless another is written |
| `1.1.1.1`, `8.8.8.8:5300`, `[2606:4700:4700::1111]:53` | in the open: UDP, and TCP for a client that came over TCP, port 53 unless another is written |

A new address set starts in Default mode. For a linked server it uses the saved DNS of that connection; a
balancer uses its own DNS or that of the selected member; direct access uses the host DNS. The read-only endpoint
`GET /api/traffic-rules/dns-defaults` returns the host's current resolver addresses. Empty default sources are
reported as missing; they do not automatically choose a public provider. Presets store explicit addresses and
custom mode accepts the forms below. Plain IP presets use plain DNS; TLS or HTTPS requires the corresponding URL.

Earlier saved resolver addresses and custom member overrides are retained when a shared address set has one
compatible DNS policy. If its former policies conflict, migration keeps the shared set and selects Default DNS from
each rule's processing method; the effective resolver addresses can change. DNS follows its rule's processing
method even where earlier independent DNS settings selected a different exit. Details of the data mapping are in
[rules.md](rules.md#migration-and-existing-configurations).

A name server asked over TLS or HTTPS is named by its address, not by a name, so nothing has to be resolved first.
The certificate it shows is checked against that address under the roots the host trusts, and a server whose
certificate does not carry the address is not asked; Cloudflare, Google and Quad9 carry theirs. The address-set API refuses an unsupported resolver form with `bad-dns`.

The connections that answered are kept for the questions that follow. The servers are asked in the order written;
one that gave no answer is asked after the ones that answer for thirty seconds, and then asked aside, so no client
waits for it to come back. Nothing falls back to plain DNS on its own: plain DNS is asked only where an entry names
it. When three questions in a row find no server, the panel says why: `fault` in the state of the resolver and the
log carry `no name server answers: https://1.1.1.1:443/dns-query: ...`, and the services count the resolver as down,
until a server answers again.

The selected processing path carries questions regardless of whether their resolver transport is plain DNS, TLS or HTTPS.

An answer larger than the datagram a client takes, 512 bytes or the size its question names, goes back cut down to
the question and marked as cut, so the client asks again over TCP and gets it whole.

The resolver also asks about the names of the rules itself, without waiting for a client: every name a rule
matches by, including the ones a `geosite:` category stands for, is asked about and the answers go into the set
of that rule. So a rule works from the moment it is saved, even for a service the client resolved earlier and
holds in its own cache. Names are asked about in batches of sixteen as they fall due, at most twice a second
and at most 256 per rule, and
each name that answered is asked again once half of `An address lives` has passed. A name that did not answer
is asked again in two seconds, and the wait doubles with every miss up to a minute, so the sets fill as soon
as the channel of the resolver carries again. A name several rules match by is asked about once per rule, so a
rule added later fills its own set without waiting for the others. Names of rules the panel no longer holds
are forgotten. Keywords and regular expressions of a
category are left alone: there is nothing to ask about.

While the routing needs the resolver and it runs on port 53 and answers on the addresses of the configurations, the
`DNS` line of every client carries the address of its own configuration instead of public name servers, and the same goes for the
links a subscription hands out. A template with name servers of its own outweighs it; a template without them
takes the resolver. When the resolver is off, answers on another port, or listens only on addresses outside the
tunnel of that configuration, the client takes what the configuration or the template names. Taking the
questions of the clients stays useful for the clients that ask elsewhere anyway, but the chain no longer rests
on it.

The address of the resolver goes into `AllowedIPs` of the client too when the ranges of its template or of its
configuration leave it out: it answers only through the tunnel.

Where the panel holds the ports of an endpoint open in the firewall of the host, it lets the clients of that
endpoint reach the resolver as well: the port of the resolver is opened on the interface of the endpoint, for UDP
and TCP, while the resolver answers on one of its addresses, see [firewall.md](firewall.md). An endpoint whose
ports the panel does not hold open is left to the host: its clients get no answer until that port is let in on
its interface, for everyone or from the whole subnet of its clients (`ufw allow in on awg0 from 10.8.0.0/24 to any
port 53`); a rule from a part of the subnet lets them not. The panel reads ufw for it and names the interfaces whose
clients ufw keeps from the resolver
(`state.keptOut` of `GET /api/dns`); the form of a template with Route by name shows them with the commands that
let them in. On the host, item 23 of the menu, subitem 13, or `amneziageo-server dns open` gives ufw
`allow in on <interface> to any port 53` for each of them, under the comment `amneziageo resolver <interface>`;
item 29 and the installation offer the same once the resolver answers. A host without ufw keeps nobody out, and a
ufw whose rules nftables shows only as matches of iptables names nothing.

The processing rule determines the socket mark used for the query. A linked server uses its own mark, and a
balancer selects an available member using its strategy. Priority uses the first available member; round can
select different members for successive queries; sticky uses the client context when it is available. Custom
member overrides are selected only for the member carrying the question. With Default mode, a balancer's own DNS
takes precedence over its member's source DNS.

A selected object that is disabled, missing or unavailable causes the question to fail instead of leaving through
the host. A default source without DNS also fails. Changing a shared address set's DNS takes effect for each rule
using it; it does not rewrite the client configuration or create another independent routing policy.

`Save` writes the settings down, and the resolver and the rules take them at `Restart`, the button that
shows in the header while saved settings wait for it. It calls `POST /api/dns/restart`, or starts the whole
panel over when its own settings wait too. `Cancel` drops what is not saved yet, and an edit left unsaved
stays while other pages are opened. `GET /api/dns` carries `pending`, true while the saved settings differ
from the ones the resolver runs with. A change of the configurations starts the resolver over on their
addresses with the settings it runs with. In the automatic mode the resolver starts and stops by itself, without
`Restart`, as a rule by name or a template with stand-in addresses comes or goes and as the clients holding a file
that names it take new ones, and as clients come to pick it or stop; `GET /api/dns` carries `needed`, `holders` and
`picked` in its `state`.

## The name servers of a file

The server picks where the `DNS` line of the files of its clients comes from, `Preferred DNS` in `DNS in client
configurations` on the `DNS` page (`filesMode` and `filesDns` of `GET /api/dns` and `PUT /api/dns`). A template and a
client may pick otherwise (`dnsMode` and `dns`); a client goes first, then its template, then the server:

| Choice | The file names |
|---|---|
| `auto` | a client or a template only: what the level above picks, the template for a client, the server for a template |
| `routing` | the resolver while the routing needs it, the name servers of the endpoint otherwise; a fresh server picks it |
| `panel` | the resolver, whether the routing needs it or not; the resolver runs while a client that is on picks it so, itself or through its template or the server |
| `network` | no `DNS` line: the device asks the name servers of its network |
| `own` | the name servers written next to the choice (`dns`, `filesDns` for the server), never the resolver |
| `endpoint` | the name servers of the endpoint of the client, `DNS` of its form |

A template with Route by name names the resolver whatever is picked. The resolver is named only where it answers:
in the automatic mode, on port 53, on an address of the configuration, and while the firewall of the host lets the
clients of the configuration in; otherwise the file takes the name servers of the endpoint, and a client of a
template `1.1.1.1` and `1.0.0.1` where the endpoint lists none. A new client and template are `auto`.

What a client of another make does with each choice, from what the clients say and do:

| Choice | Full tunnel (`0.0.0.0/0`) | Chosen networks |
|---|---|---|
| `panel` | the questions go to the panel through the tunnel; the panel uses the resolver transport configured by the matching address set | the same: the address of the resolver goes into `AllowedIPs` |
| `network` | the device asks its network past the tunnel, in the open; a name server outside the subnet of the device (mobile data) is asked through the tunnel, where the panel takes the question on port 53 while the resolver answers the endpoint; AmneziaWG for Windows locks that out in a full tunnel and resolves no name | the device asks its network past the tunnel |
| `own`, `endpoint`, `routing` without the resolver | the questions go through the tunnel, and while the resolver answers the endpoint the panel takes them on port 53; with a rule by name it also refuses DNS over TLS and HTTPS, so Android falls back to port 53, and without one Android asks a server that speaks DNS over TLS that way, past the resolver | the questions go past the tunnel to those servers |

Routing by name on the server sees only the questions that reach the resolver: `network` never, `own` and `endpoint`
when the servers lie outside the ranges of the client.

AmneziaVPN takes the IPv4 addresses of the `DNS` line from the `vpn://` link and asks them, while from the file it
takes the line only when it holds two IPv4 addresses. Without an IPv4 address, that is with no line, which is what
`network` writes, or with an IPv6 one alone, it asks the name servers of its own settings, `1.1.1.1` and `1.0.0.1`
unless changed, through the tunnel in a full tunnel, where the panel takes the questions on port 53 as above while
the interception of the questions of the clients is on. Its leak guard, on unless turned off, lets DNS reach those
servers only.

The update that brought the choice of the server gave the server the choice of the endpoint with the most clients
(`auto` of an endpoint became `routing`, `own` became `endpoint`), and gave the clients of the other endpoints that left
it to the level above the choice of their endpoint, so the files handed out before the update do not change. The
choice of an endpoint is gone: its `DNS` is the name servers of the endpoint. The template a fresh panel starts with is
`own` with `1.1.1.1` and `1.0.0.1`.

Our client learns the choice from the services of the server (`dns` among the features: `mode`, the `servers` of the
file and the `resolver`, the addresses the resolver answers it on inside the tunnel while it runs), see
[services.md](services.md).

## Files that name the resolver

A file, a QR code or a subscription handed out while the routing needs the resolver carries its address in the `DNS`
line, and a device keeps asking that address until it takes a new file. The panel notes for every client whether the
file it was handed last names the resolver: `GET /api/clients/{id}/config` notes it for that client, and a
subscription for the clients it carries each time it is read. In the automatic mode the resolver keeps running while
a client that is on holds such a file, so the routing can stop needing it without those clients losing their names.
While it runs for those files only, the files handed out name the name servers of the configuration or the template
again, and the resolver stops by itself once every such client took a new one.

`GET /api/dns` carries their number in `holders` of its `state`; `amneziageo-server dns show` and
`amneziageo-server dns get holders` print it. `Off` stops the resolver whatever the files name: the clients that hold
a file naming it get no answer to a name until they are handed their files anew, and the command that turns it off
and item 29 of the menu say how many they are.

The update that brought this counts every client of a panel whose resolver answered on port 53 before as holding a
file that names it, except a client whose template names name servers of its own and answers with no stand-in
addresses.

## What contradicts

`GET /api/dns/conflicts` says who needs the resolver (`needs`: the ordered rules matching names, the templates with
Route by name, the clients that pick it and the clients that hold a file naming it) and what the settings that decide
the `DNS` line of the clients contradict (`findings`). A finding carries a `code`, a `level`, the settings that bring
it (`subjects`: `dns`, `rule`, `basic`, `template`, `config` or `client`, with the number and the name) and the
clients it touches, each with where its name servers were picked and what they are:

| Code | Level | When |
|---|---|---|
| `rule-off` | `error` | a rule matching names while the resolver is off: it does not work |
| `standins-off` | `error` | a template with Route by name while the resolver is off |
| `holders-off` | `error` | clients hold a file that names the resolver while it is off: they get no answer to a name |
| `panel-off` | `warning` | a client, a template or the server picks the resolver while it is off: the files take other name servers |
| `port` | `warning` | the resolver answers on a port other than 53: no file names it |
| `listen` | `warning` | the resolver does not listen on an address of a configuration: its files do not name it |
| `kept` | `warning` | the firewall of the host keeps the clients of a configuration from the resolver: their files do not name it |
| `rule-outside` | `warning` | a client a rule by name takes asks name servers outside its tunnel or leaves them to its network: the resolver does not see its questions, and the rule takes only the names the resolver asks about itself |
| `outside` | `hint` | a client asks name servers outside its tunnel while no rule by name takes it |
| `unseen` | `hint` | the clients whose template or the server leaves their questions outside the tunnel while no rule by name takes them: routing by name would not see them |

`POST /api/dns/check` takes the draft of a client, a template, an endpoint, a rule or the settings of the resolver
(`client`, `template`, `config`, `rule` or `dns`, with `id` for one the panel holds) and answers what it would
contradict, so a form shows it before the save. For a client it also returns `line`, the name servers its file would
name and where they were picked, and the hint `outside` when they lie outside its tunnel and no rule by name takes
it; for an endpoint, the line of a client with nothing of its own there; for the settings of the resolver, such a line
at every endpoint that is on, with the choice of the draft. For a template `lines` holds such a line at
every endpoint that is on where its clients are, or at every endpoint that is on while it has none there, and `line`
is the first of them that keeps the resolver. Every line names its `endpoint` and `lost`, whether the file wants the
resolver and loses it to the port, its addresses or the firewall. A template draft keeps the
ranges of the template it changes while its entries stay the same; other entries are worked out as the preview does.
A save is never refused for a contradiction.

## Turning the resolver on

| Where | How |
|---|---|
| the installation | no question: a fresh panel is automatic; `resolver` set to `no` in a file of answers keeps it off, see [install.md](install.md#the-menu) |
| the menu | item 29, `DNS for Clients` |
| the script | `amneziageo-server dns on`, `amneziageo-server dns off`, `amneziageo-server dns` to be asked |
| the utility | `dns show`, `dns get enabled port files files-dns holders`, `dns set --enabled auto` (or `on`) or `off`, and `dns set --files routing \| panel \| network \| own \| endpoint` with `--files-dns <servers>` for `own` |
| the panel | `Mode` in `DNS server for clients` on the `DNS` page, then `Restart` |

The menu and the commands work the same on a package and in a container: there they go through
`docker compose exec panel amneziageo-server dns on`. They save the setting and have the running panel start the
resolver over at once; a panel that is stopped takes it when it starts. Turned off, the resolver does not run, holds
no port and keeps no connection to a name server. The update that brought the automatic mode moved every panel to it;
later updates leave the mode where it was.

A panel that has no endpoint yet has no address to answer on: the resolver waits, `the resolver does not run yet:
there is no address to answer the clients on`, and starts with the first endpoint.

## What the host gets

Once the resolver runs, the host gets two more things.

The chain `resolve` sits on the nat prerouting hook and sends every question that arrives on an interface
of a configuration to the resolver:

```
chain resolve {
    type nat hook prerouting priority dstnat; policy accept;
    iifname != { "awg1" } accept
    meta l4proto { tcp, udp } th dport 53 redirect to :53
}
```

Intercepted questions use the DNS policy of the matching processing rule. Their original destination does not
create an independent DNS route. To query a private resolver behind a linked server, select that server in the rule
and use its imported DNS or an explicit custom resolver address in the address set. Direct processing uses the host
DNS in Default mode, including local resolvers configured on the host.

The guards that keep a client off the name servers it carries its own way, since a question inside TLS or HTTPS is
a question the resolver never sees, live in a table of their own, `amneziageo_dns`, laid after the rules and never
laid anew with them:

```
chain heard {
    type filter hook prerouting priority mangle; policy accept;
    iifname != { "awg1" } accept
    meta nfproto ipv4 ct state new meta l4proto { tcp, udp } th dport 53 update @asked4 { ip saddr }
}

chain guard {
    type filter hook forward priority filter - 4; policy accept;
    iifname != { "awg1" } accept
    ct state != new accept
    ip saddr @free4 accept
    meta nfproto ipv4 ip saddr != @asked4 meta l4proto { tcp, udp } th dport 853 update @tried4 { ip saddr limit rate over 2/minute burst 3 packets } add @free4 { ip saddr } accept
    ip saddr != @asked4 ip daddr @doh4 meta l4proto { tcp, udp } th dport 443 update @tried4 { ip saddr limit rate over 2/minute burst 3 packets } add @free4 { ip saddr } accept
    meta nfproto ipv4 meta l4proto { tcp, udp } th dport 853 jump refuse
    ip daddr @doh4 meta l4proto { tcp, udp } th dport 443 jump refuse
}

chain refuse {
    meta l4proto tcp reject with tcp reset
    reject
}
```

The same lines stand for IPv6 with `asked6`, `tried6`, `free6` and `doh6`. The sets `doh4` and `doh6` hold the
addresses of the public name servers that answer over HTTPS, `asked4` the clients that asked port 53 in the last
half hour, `free4` for a day the clients the guards let go. A client is refused at once, so one that falls back to
plain DNS does it without waiting and lands in `asked4`; one that never asks port 53 and tries a name server of its
own three times in a short while is let go. The lines take only the clients the rules by name take: a rule limited
to interfaces or clients puts its conditions in front of them.

The table stands while the resolver answers, a rule on the host matches by name and the panel reads
no names out of the connections; otherwise the panel takes it away. A host that does not take the table gets a line
in the log, and the rules stay as they were laid.

## A server that leaves through another

A linked server is a VPN client of the remote server. A rule selecting it carries its DNS questions through that
same connection. Default mode commonly uses the remote resolver address imported in the linked-server profile;
custom mode can instead name another resolver reachable by that connection. The remote server's own DNS
interception, routing and guards continue to apply. Choosing a remote server does not discover an unconfigured
resolver address automatically.

## The sets of the rules

Every rule that matches by name has the sets `n<id>v4` and `n<id>v6` with a timeout of its own. The
resolver reads the answer, takes the name of the question and the name of every record in it, asks the
rules which of them match, and queues the addresses. An answer that brings an address the sets do not hold
yet goes back to the client once the address is in the set, so the first connection to it already takes the
rule; it waits for that half a second at most. The rest of the queue goes to the host half a second after an
answer and every five seconds otherwise, up to
512 addresses in one command, and an address already there is not sent again until half its life has
passed. Addresses of a rule that is gone or turned off are dropped instead of sent. The rules go to the host as
a whole table, and every address whose time has not run out goes in the same step, with the time it has left,
so the sets never stand empty. When the server starts, it reads the sets back from the host before it lays the
rules for the first time, so the addresses outlive a restart too.

A rule keeps the newest 128 of its addresses standing. They go into the database of the panel once a minute,
come back into the set before the first question is asked and take a fresh time of their own while they stand,
so a machine that rebooted does not leave the rule empty and a name that stopped answering leaves its
addresses behind. An address a newer one pushes out of the standing ones lives out its time and goes.

## Stand-in addresses

The clients of a template with Route by name on (`standIns`, [templates.md](templates.md)) are answered about the
names of the template with stand-in addresses out of `198.19.0.0/16`. The resolver asks the name as it does for any
client, so the rules take the real addresses into their sets and the connection log knows the name. The client gets
one `A` record with the stand-in address and a lifetime of at most 300 seconds; `AAAA` and `HTTPS` come back empty. A
name that leads to a name of the template through an alias takes a stand-in too.

The host holds the map `stand` in the table `amneziageo_rt`. A new connection of a client to a stand-in goes to the
real address (`dnat`), the rules decide it by the real address in a chain right after that, and a stand-in the map
does not hold is refused with a reset. The panel keeps the names with their stand-ins in its database ten seconds
after they change and when it stops, and lays them again when it starts; a name nobody asked about for seven days
lets its address go.

## Names read out of the connections

An answer of the resolver is not the only place a name shows. A client that asks a name server of its own, or
any client while the resolver is off, goes to an address no answer of the panel ever carried, and the set of a
rule by name would not hold it. So while a rule on the host matches by name, the panel reads the
name out of the connection itself, the resolver on or off: the server name of the first message of TLS on TCP
443, the `Host` header of a request on TCP 80, and the server name inside the first packets of QUIC on UDP 443.
The address goes into the sets of the rules the name falls under, the way an answered address does, and stays
for `An address lives`.

The reading is off unless `Names:IsEnabled` is set to `true` (the variable `Names__IsEnabled`). While it is off,
the set of a rule by name holds only the addresses the resolver answered with: the guards keep the clients on the
resolver, see [What the host gets](#what-the-host-gets), and a client that asks a name server they do not know goes
the way the rules send the address.

The firewall shows the first packets to the panel in queue 7318. The connections themselves stay in the kernel,
and the queue is taken with `bypass`: while the panel does not run, the kernel passes the packets on by itself
and the rules decide by the addresses they hold.

**QUIC.** Once the rules have decided the first packet of a QUIC connection of a client, it waits in the queue.
The panel opens it, the keys come from the packet itself, puts the first message together out of as many as
eight packets, waits for the rest no longer than 60 milliseconds, lays the address into the sets and sends the
packets through the chain once more: they are decided anew, so the first packet already leaves the way the rule
says. The connection then stays out of the queue for 30 seconds (the sets `read4` and `read6`).

**TCP.** The firewall hands the first connection to an address over to the panel, see below. A connection it does
not hand over is read in the queue: one to port 80 or 443 that reaches a rule by name whose set does not hold the
address, and that goes to an address the panel has not looked at in the last five minutes (the sets `seen4` and
`seen6`), is noted (the sets `wait4` and `wait6`, ten seconds) and goes on the way the rules send it by the
address. What its client says first waits in the queue. A name that falls under no rule, or under the rule the
connection already follows, lets it go on untouched. A name the rules send another way has its address laid into
the sets, and the connection goes on the way it took: it is neither moved nor reset, the rules take the next
connections to that address. A name a blocking rule takes has the connection reset and its address laid into the
set of the rule, so the kernel drops the next ones; no more than eight connections of a client to an address are
reset a minute.

**Taking a connection over.** So that a rule by name holds from the first connection, the firewall hands the
first connection to an address over to port 7319 of the panel (TPROXY, mark `0xa500`, routing table 42500, rule
9990). The panel answers the client itself, so nothing of the connection leaves the host before its name is
read: it waits up to 300 milliseconds for what the client says first, reads the name, decides by the rules the
way the route tester does, lays the address into the sets and carries the connection itself: through the
outbound of the rule, through the host, or not at all for a blocking rule. A site the usual way out of the host
does not reach therefore opens at the first try when a rule by name sends it through an outbound. The next
connections to that address are decided by the kernel. A connection the panel carries stays with it until it
ends, and ends when the panel is started over. Only connections to ports 80 and 443 of public addresses are
taken over, and no more than 300 a second: an address of the host itself, a private, shared or link-local range,
a client of an endpoint and a range the endpoint of the client keeps its clients out of are left to the kernel,
since a connection the panel carries leaves as the host's own and the rules of the host for forwarded packets do
not see it. The panel listens for them on the loopback alone. While it carries 4096 connections the firewall
hands it no more until a quarter of them ended; what comes meanwhile is read in the queue.

A client that says nothing in those 300 milliseconds is slow, or it waits for the far end to speak first. The
panel then reaches the far end the way the rules send the address without a name and waits for the first word
of either. When the far end speaks first the connection stays on that way. When the client speaks, its name
decides as it would have at once: a name the rules send another way has the panel reach the far end anew along
it, and the rest of what a client started to say is waited for up to ten seconds. An address counts as looked
at only once the client or the far end said something over a connection to it: a connection either of them
ended without a word, or nobody spoke over, leaves the address to the next look.

A connection handed over arrives on the host for an address that is not its own. ufw drops such packets before
it reads its rules (the chain `ufw-not-local`). On a host guarded by ufw the firewall therefore sends the
connection in through a TCP port ufw lets in, the one of the panel or of a service of an endpoint (the chain
`door` redirects what carries the mark `0xa500` to it): the kernel still gives the connection to the socket of
port 7319, and the panel asks it where the connection was going. Nothing is added to ufw, and the panel names the
port in its log when it starts. When ufw lets in no port the panel holds, the panel takes nothing over and reads
TCP in the queue. On any host, when packets are handed over and no connection reaches the panel across two checks
three seconds apart, the panel has them sent in through such a port, and when that does not help either it stops
the handing over and goes on in the queue; IPv6 is watched apart from IPv4. `Names:Take` set to `false` (the
variable `Names__Take`) turns the taking over off: every first connection then goes the way its address sends it.

What follows from the address being what the kernel goes by:

- an address stays looked at for five minutes, so another site on the same address is decided by the address
  meanwhile, and a site on an address a rule already holds goes with the rule, whatever its own name;
- a connection that hides its name (ECH) shows only the name of the provider in front of it, and a connection
  that carries no name at all, to a bare address or in a protocol of its own, shows none: both are decided by
  the address.

The kernel loads `nft_queue`, `nfnetlink_queue`, `nft_tproxy`, `nft_fib` and `nft_redir` itself when the ruleset
first names them, and resetting a connection from that chain takes Linux 5.9. A host that cannot take the
ruleset with these lines gets it first without the taking over, then without the reading, and `fault` in the
state of the resolver says why the names are not read. `Names:IsEnabled` set to `true` (the variable
`Names__IsEnabled`) turns the reading on, `Names:Port` moves the port, `Names:Most` sets how many connections
the panel carries at once.

`GET /api/dns` carries `reading`: whether the queue and the port are taken (`queue`, `port`), how many packets
the queue showed and how many connections gave their name there (`packets`, `named`), how many TCP connections a
blocking rule cut (`reset`), how many stayed on a way the rules do not name (`missed`), how many were taken over
and how many are carried right now (`taken`, `open`), and how many addresses the names brought into the sets
(`learned`). The connection log writes the connections the panel carried with their names and what became of
them, see [diagnostics.md](diagnostics.md).

## What the panel shows

The page carries the settings and, above them, what the resolver is doing: whether it runs, the addresses
it took, how many questions were asked, how many were answered out of memory, how many no name server
answered, how many addresses went into the sets, how many wait, and how many rules match by name. A fault of
the reading of names shows in the same line as a fault of the resolver, when the resolver has none of its own.
