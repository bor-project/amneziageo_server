# The resolver

The panel answers the name questions of the tunnel clients itself. It passes a question on to a name
server outside, gives the answer back, and puts the addresses it saw into the sets of the routing rules
that match the name. This is what makes a rule by domain work: the kernel decides by address, and the
answer of the resolver is where the addresses come from.

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
| Upstream servers | the name servers questions are passed to, `1.1.1.1` or `8.8.8.8:5300` |
| Ask through the channel | the outbound or the balancer the questions leave through, empty for the way out of the host |
| An address lives | how long an answered address stays in the set of a rule, in minutes |
| Answers held in memory | how many answers are kept back, zero for none |
| An answer lives from, to | the bounds the lifetime of a kept answer is clamped to, in seconds |
| Take the questions of the clients | whether the questions sent elsewhere are redirected to the resolver |
| Stop DNS over TLS | whether port 853 is dropped for the clients |
| Stop DNS over HTTPS | whether port 443 to the known name servers is dropped for the clients |

A port under 1024 needs `CAP_NET_BIND_SERVICE`: under an ordinary account the resolver reports
`Permission denied` and takes no address. An address that is not on the host is skipped, and the
resolver answers on the rest.

The resolver also asks about the names of the rules itself, without waiting for a client: every name a rule
matches by, including the ones a `geosite:` category stands for, is asked about and the answers go into the set
of that rule. So a rule works from the moment it is saved, even for a service the client resolved earlier and
holds in its own cache. Names are asked about in batches of sixteen twice a second, at most 256 per rule, and
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

## The sets of the rules

Every rule that matches by name has the sets `n<id>v4` and `n<id>v6` with a timeout of its own. The
resolver reads the answer, takes the name of the question and the name of every record in it, asks the
rules which of them match, and queues the addresses. An answer that brings an address the sets do not hold
yet goes back to the client once the address is in the set, so the first connection to it already takes the
rule; it waits for that half a second at most. The rest of the queue goes to the host twice a second, up to
512 addresses in one command, and an address already there is not sent again until half its life has
passed. Addresses of a rule that is gone or turned off are dropped instead of sent. The rules go to the host as
a whole table, and every address whose time has not run out goes in the same step, with the time it has left,
so the sets never stand empty. When the server starts, it reads the sets back from the host before it lays the
rules for the first time, so the addresses outlive a restart too.

## What the panel shows

The page carries the settings and, above them, what the resolver is doing: whether it runs, the addresses
it took, how many questions were asked, how many were answered out of memory, how many no name server
answered, how many addresses went into the sets, how many wait, and how many rules match by name.
