# The resolver

The panel answers the name questions of the tunnel clients itself. It passes a question on to a name
server outside, over HTTPS unless told otherwise, gives the answer back, and puts the addresses it saw into the sets
of the routing rules that match the name. This is what makes a rule by domain work: the kernel decides by address,
and the answer of the resolver is where the addresses come from. It also keeps what the clients ask about from
leaving the server in the open.

A fresh panel holds the resolver off: the clients are handed the name servers of their configuration and ask them
through the tunnel, and those questions leave the server as plain DNS. [Turning it on](#turning-the-resolver-on)
takes a question of the installation, an item of the menu or a command.

## Rights

| Route | Right |
|---|---|
| `GET /api/dns` | `state:read` |
| `PUT /api/dns` | `routing:write` |
| `POST /api/dns/restart` | `routing:write` |

## The settings

| Setting | Holds |
|---|---|
| Answer the clients | whether the resolver runs |
| Port | the port questions are taken on, 53 by default |
| Listen on | the addresses questions are taken on, empty for the addresses of the configurations |
| Upstream servers | the name servers questions are passed to and the way each is asked, see [How the name servers are asked](#how-the-name-servers-are-asked) |
| Ask through the channel | the outbound or the balancer the questions leave through, empty for the way out of the host |
| An address lives | how long an answered address stays in the set of a rule, in minutes |
| Answers held in memory | how many answers are kept back, zero for none |
| An answer lives from, to | the bounds the lifetime of a kept answer is clamped to, in seconds |
| Take the questions of the clients | whether the questions sent elsewhere are redirected to the resolver |
| Stop DNS over TLS | whether port 853 is dropped for the clients while a rule matches by name |
| Stop DNS over HTTPS | whether port 443 to the known name servers is dropped for the clients while a rule matches by name |

A port under 1024 needs `CAP_NET_BIND_SERVICE`: under an ordinary account the resolver reports
`Permission denied` and takes no address. An address that is not on the host is skipped, and the
resolver answers on the rest.

## How the name servers are asked

An entry of `Upstream servers` names a name server by its address and tells how it is asked:

| Entry | Asked |
|---|---|
| `https://1.1.1.1/dns-query`, `https://9.9.9.9:5053/dns-query` | DNS over HTTPS: a `POST` of `application/dns-message`, port 443 and the path `/dns-query` unless others are written |
| `tls://1.1.1.1`, `tls://1.1.1.1:853` | DNS over TLS, port 853 unless another is written |
| `1.1.1.1`, `8.8.8.8:5300`, `[2606:4700:4700::1111]:53` | in the open: UDP, and TCP for a client that came over TCP, port 53 unless another is written |

A fresh panel asks `https://1.1.1.1/dns-query` and `https://8.8.4.4/dns-query`. A panel that saved its name servers
before keeps them as they are, plain ones included, until they are changed here, in the menu or with
`amneziageo-server dns set --upstreams default`.

A name server asked over TLS or HTTPS is named by its address, not by a name, so nothing has to be resolved first.
The certificate it shows is checked against that address under the roots the host trusts, and a server whose
certificate does not carry the address is not asked; Cloudflare, Google and Quad9 carry theirs. `PUT /api/dns`
refuses an entry that is none of the three forms with `bad-upstream`.

The connections that answered are kept for the questions that follow. The servers are asked in the order written;
one that gave no answer is asked after the ones that answer for thirty seconds, and then asked aside, so no client
waits for it to come back. Nothing falls back to plain DNS on its own: plain DNS is asked only where an entry names
it. When three questions in a row find no server, the panel says why: `fault` in the state of the resolver and the
log carry `no name server answers: https://1.1.1.1:443/dns-query: ...`, and the services count the resolver as down,
until a server answers again.

`Ask through the channel` carries the questions the same way whatever they travel in.

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

While the resolver runs on port 53 and answers on the addresses of the configurations, the `DNS` line of every
client carries the address of its own configuration instead of public name servers, and the same goes for the
links a subscription hands out. A template with name servers of its own outweighs it; a template without them
takes the resolver. When the resolver is off, answers on another port, or listens only on addresses outside the
tunnel of that configuration, the client takes what the configuration or the template names. Taking the
questions of the clients stays useful for the clients that ask elsewhere anyway, but the chain no longer rests
on it.

Where the panel holds the ports of an endpoint open in the firewall of the host, it lets the clients of that
endpoint reach the resolver as well: the port of the resolver is opened on the interface of the endpoint, for UDP
and TCP, while the resolver answers on one of its addresses, see [firewall.md](firewall.md). An endpoint whose
ports the panel does not hold open is left to the host: its clients get no answer until that port is let in on
its interface by hand.

The questions leave through the way out of the host until a channel is picked. Where the local network
answers about a service differently from the network behind the channel, a rule by name fills its set with
the wrong addresses or with none at all, and the traffic never reaches the channel the rule sends it to.
`Ask through the channel` puts the mark of that outbound on the questions, the mark its probe already
carries, so the answers come from where the traffic is going. `PUT /api/dns` refuses a channel the panel
does not hold with `unknown-outbound`. The entries of the client templates are resolved the same way.

The channel is picked again for every question. A balancer hands the questions to its first member that
carries traffic, and a `round` balancer hands them out to the members that carry in turn; a `sticky` one acts
as `priority`, since every question comes from the host itself. So the resolver outlives the death of one
channel the way the rules do. When none of the members carries, or the one outbound picked carries nothing,
the questions still go into the channel, and `fault` in the state of the resolver says what is wrong.

A question is never sent through the way out of the host once a channel is picked. While the outbound is
turned off, the balancer is turned off or holds no outbound that is on, or the panel holds nothing under the name,
the resolver asks nothing and answers the clients with `SERVFAIL`; the same goes for the names of the rules it
asks about itself, the route tester and the entries of the templates. `fault` then names the reason, and the
panel shows it on the `DNS` page.

`Save` writes the settings down, and the resolver and the rules take them at `Restart`, the button that
shows in the header while saved settings wait for it. It calls `POST /api/dns/restart`, or starts the whole
panel over when its own settings wait too. `Cancel` drops what is not saved yet, and an edit left unsaved
stays while other pages are opened. `GET /api/dns` carries `pending`, true while the saved settings differ
from the ones the resolver runs with. A change of the configurations starts the resolver over on their
addresses with the settings it runs with.

## Turning the resolver on

| Where | How |
|---|---|
| the installation | `Answer the DNS of its clients on this server, asking outside over HTTPS?`, asked with the first endpoint, `resolver` in a file of answers, see [install.md](install.md#the-menu) |
| the menu | item 29, `DNS for Clients`; the status under the menu shows `DNS: on, asks outside encrypted` |
| the script | `amneziageo-server dns on`, `amneziageo-server dns off`, `amneziageo-server dns` to be asked |
| the utility | `dns show`, `dns get enabled port upstreams encrypted outbound`, `dns set --enabled on` or `off`, `dns set --upstreams <servers>` or `default` for the ones a fresh panel asks |
| the panel | `Answer the clients` on the `DNS` page, then `Restart` |

The menu and the commands work the same on a package and in a container: there they go through
`docker compose exec panel amneziageo-server dns on`. They save the setting and have the running panel start the
resolver over at once; a panel that is stopped takes it when it starts. Turned off, the resolver does not run, holds
no port and keeps no connection to a name server. An update leaves the switch where it was.

A panel that has no endpoint yet has no address to answer on: the resolver waits, `the resolver does not run yet:
there is no address to answer the clients on`, and starts with the first endpoint.

## What the host gets

The ruleset the rules build carries two more things once the resolver runs.

The chain `resolve` sits on the nat prerouting hook and sends every question that arrives on an interface
of a configuration to the resolver:

```
chain resolve {
    type nat hook prerouting priority dstnat; policy accept;
    iifname != { "awg1" } accept
    meta l4proto { tcp, udp } th dport 53 redirect to :53
}
```

The decision chain starts with the lines that keep a client off the name servers it carries its own way,
because a question inside TLS or HTTPS is a question the resolver never sees:

```
meta l4proto { tcp, udp } th dport 853 drop
ip daddr @doh4 meta l4proto { tcp, udp } th dport 443 drop
ip6 daddr @doh6 meta l4proto { tcp, udp } th dport 443 drop
```

The sets `doh4` and `doh6` hold the addresses of the public name servers that answer over HTTPS.

These lines and both sets go on the host only while a rule or a basic list on it matches by name, since the answers
the resolver sees are what such a rule goes by. A server none of whose rules matches by name lays none of them,
whatever the two switches say: a client that carries its questions inside TLS or HTTPS goes on asking, and so does
another AmneziaGeo server that leaves through this one and asks its name servers over HTTPS.

## A server that leaves through another

A panel whose outbound is a tunnel to another AmneziaGeo server is a client of that server, and with `Ask through
the channel` its questions arrive there like the traffic of any client. The server at the far end passes them on
while none of its own rules matches by name. Where it does hold such a rule, its guards drop a question carried
over TLS or HTTPS. The near panel then names its upstream in the open, as `1.1.1.1`, and goes on asking through the
channel: the tunnel carries the question to the far end, whose resolver takes it, while it takes the questions of
the clients, and asks outside over HTTPS.

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

## Names read out of the connections

An answer of the resolver is not the only place a name shows. A client that asks a name server of its own, or
any client while the resolver is off, goes to an address no answer of the panel ever carried, and the set of a
rule by name would not hold it. So while a rule or a basic list on the host matches by name, the panel reads the
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
