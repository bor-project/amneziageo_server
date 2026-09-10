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
| An address lives | how long an answered address stays in the set of a rule, in minutes |
| Answers held in memory | how many answers are kept back, zero for none |
| An answer lives from, to | the bounds the lifetime of a kept answer is clamped to, in seconds |
| Take the questions of the clients | whether the questions sent elsewhere are redirected to the resolver |
| Stop DNS over TLS | whether port 853 is dropped for the clients |
| Stop DNS over HTTPS | whether port 443 to the known name servers is dropped for the clients |

A port under 1024 needs `CAP_NET_BIND_SERVICE`: under an ordinary account the resolver reports
`Permission denied` and takes no address. An address that is not on the host is skipped, and the
resolver answers on the rest.

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
rules which of them match, and queues the addresses. The queue goes to the host twice a second, up to
512 addresses in one command, and an address already there is not sent again until half its life has
passed. Addresses of a rule that is gone or turned off are dropped instead of sent.

## What the panel shows

The page carries the settings and, above them, what the resolver is doing: whether it runs, the addresses
it took, how many questions were asked, how many were answered out of memory, how many no name server
answered, how many addresses went into the sets, how many wait, and how many rules match by name.
