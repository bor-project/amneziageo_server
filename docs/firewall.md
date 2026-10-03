# The ports of the panel in the firewall of the host

The panel writes its own nftables tables, and a table of its own says nothing to the firewall the host
already runs: a `drop` in the chain of ufw ends the packet whatever another table accepts. So a port is
opened where the host closes it, in ufw itself.

Every port stays closed until it is asked for, and nothing is asked for by default. The ports of an endpoint, the
port of the panel and the port of the subscriptions are held open from the menu of the server, `amneziageo-server`,
item 23 `Firewall Management`, or by `amneziageo-server endpoint open <name>`,
`amneziageo-server panel set --opened on` and `amneziageo-server subscriptions set --opened on`, and from the page of
the panel by the button next to a closed port, see [A closed port in the panel](#a-closed-port-in-the-panel). Nothing
changes on a host that was set up by hand until one of them goes on.

The page of the panel turns these switches on and never off. `PUT /api/configs/{id}`, `PUT /api/panel` and
`PUT /api/subscription` without `opened` keep the one held, so a form saved after a change in the menu does not undo
it; a request that names `opened` changes it.

## What is opened

| Toggle | What it opens |
|---|---|
| An endpoint, item 23 of the menu or the button by its port, see [configs.md](configs.md) | its UDP port, the TCP port of its services, see [services.md](services.md), and both ways through its interface, so what its clients send and what comes back to them passes |
| The panel, item 23 of the menu or the button by its port, see [serving.md](serving.md) | the port the panel binds, unless it binds the loopback alone |
| The subscriptions, item 23 of the menu or the button by their port, see [subscriptions.md](subscriptions.md) | the port they are served on, while they are handed out on a port of their own |

Both families are opened together, since ufw takes a rule for each of them.

## How it is opened

Where the host carries ufw, the panel gives it the rules and marks each one with a comment:

```
ufw allow 51820/udp comment 'amneziageo awg0'
ufw allow 51820/tcp comment 'amneziageo awg0'
ufw route allow in on awg0 comment 'amneziageo awg0'
ufw route allow out on awg0 comment 'amneziageo awg0'
ufw allow 8443/tcp comment 'amneziageo panel'
```

The comment is what the panel knows its own rules by. It reads `ufw show added` before every change, adds
what is missing and takes out only the rules carrying its mark that nothing asks for any more. A rule
written by hand stays where it is, with or without a comment of its own, and so does a rule the panel handed over
to the host, `kept by amneziageo <name>`, see [A port that moves](#a-port-that-moves).

A ufw that is turned off takes the rules all the same and holds them until it is turned on, so `ufw enable`
on a running server does not cut the tunnels off. The panel never turns ufw on or off itself.

Where the host carries no ufw, the panel lays the table `inet amneziageo_open` instead, with the same ports
in its `input` chain and the interfaces in its `forward` chain. On a host that closes nothing this changes
nothing, and on a host closing ports in a table of its own the ports are still to be opened there by hand.

## Whether the panel may change the firewall

`GET /api/firewall` takes the right `state:read` and answers
`{"engine":"ufw","able":true,"reason":"","message":""}`. `engine` is what the panel holds its ports open with, `ufw`
or `nft`; `able` tells whether it may change the firewall now; `reason` says what keeps it from doing so, and
`message` carries what the host answered.

| Engine | The panel may change the firewall when | Otherwise `reason` is |
|---|---|---|
| `ufw`, the host carries the ufw binary | `ufw show added`, which reads the rules and wants root, goes through | `no-rights`, with what ufw said |
| `nft`, no ufw binary, as in the image | no chain `ufw-user-input` stands in `ip filter`, and `nft -c` takes the table of the open ports | `host-ufw` when the chain is there, `no-rights` when the dry run is refused |

`host-ufw` is a host that runs ufw out of the reach of the panel, as around a container: a `drop` of that ufw
outweighs the table of the panel, so the port is opened in the ufw of the host, by hand.

## A closed port in the panel

The tabs `Server` and `Subscriptions` of the settings ask the firewall of the host whether their port is let in, and
the form of an endpoint that is turned on asks the same about its UDP port and the TCP port of its services. A closed
port is named under its field. The list of the endpoints names the closed ports of an endpoint that is turned on
under its connection address.

Where `GET /api/firewall` says the panel may change the firewall, a role that may change what holds the port,
`interfaces:write` for an endpoint and `access:write` for the panel and the subscriptions, finds a button next to the
closed port:

| Port | Button | What it does |
|---|---|---|
| a port that is saved | `Open` | holds the port open at once; the list of the endpoints carries it as well |
| a port that is new or moved | `Open on save` | sends `opened: true` with the save; `Do not open` takes it back |

A new port of something whose ports the panel holds open already is not asked about: the panel opens it once the form
is saved, and says so under the field.

`Open` calls one route for each owner. The route turns the switch on, settles the ports at once and answers what a
save answers; when the firewall refuses, it answers 409 with `firewall-refused` and what the host said, and the switch
stays on for the next settle.

| Route | Right |
|---|---|
| `POST /api/configs/{id}/open` | `interfaces:write` |
| `POST /api/panel/open` | `access:write` |
| `POST /api/subscription/open` | `access:write` |

Where the panel may not change the firewall, or for a role without the right, the closed port is named with where it
is opened by hand: item 23 of the menu, subitem 6 for an endpoint, or `ufw allow <port>` on the host behind
`host-ufw`. A new port the firewall closes on the tabs `Server` and `Subscriptions` is not saved until it is opened,
by the button or by hand, so the panel does not move where nobody reaches it; a port that stays as it was is only
named. An endpoint is saved all the same, since the ports of an endpoint that is already there are opened from its
form or the menu.

## A port that moves

A port the panel holds open that moves to another number is opened at the new number once the change is saved, and
the old one is closed. Where the host carries ufw and the panel may change it, the forms ask first: `Close the
former port 51820/udp?` `Close` takes the old port out; `Leave open` sends `keep: true` with the save.

`PUT /api/configs/{id}`, `PUT /api/panel` and `PUT /api/subscription` take `keep`. With `keep: true` every port the
panel held open before the save and holds open no longer is handed over to the host instead of closed; a port that
only passed to another endpoint, the panel or the subscriptions is still held, and stays under the panel. Right
before the ports are settled, the panel gives ufw its own rule for the port again, under the comment
`kept by amneziageo <name>`, the name of what held it:

```
ufw allow 51820/udp comment 'kept by amneziageo awg0'
```

ufw takes a rule that differs only in its comment as the same rule and puts the new comment on it in place. The
comment no longer starts with the mark of the panel, so from then on the rule belongs to the host: the panel never
takes it out, and ufw keeps it across restarts. `ufw delete allow 51820/udp` takes it out by hand. A port whose rule
the host held already, under a comment of its own, is left as it is.

The table `inet amneziageo_open` keeps nothing: it holds the plan of the moment and nothing else, so where the host
carries no ufw the old port closes whatever `keep` says, and the forms do not ask. A request without `keep` closes
the old port. When ufw refuses to keep a port, the log says why, the port stays open under the panel until the next
settle, and the save goes through all the same.

## How the state of a port is read

Where the host carries ufw, the panel reads `ufw status verbose`: the first rule that names the
port decides, the policy for what comes in otherwise. Where it does not, as in a container, the panel reads the
chains ufw leaves in nftables, `ufw-user-input` and the policy of `INPUT`. Where iptables before 1.8.9 wrote the
rules of ufw, as on Ubuntu 22.04 and Debian 11, they stay matches of iptables in nftables, and nft shows their ports
only with the extensions of iptables beside it, which the image carries. A rule nft still shows as `xt match "tcp"`
without the port leaves a port that no rule ahead of it names unknown, and nothing is named. A host with neither
tells nothing, and nothing is named.

`GET /api/firewall/port?port=<port>`, with `&protocol=udp` for a UDP port, takes the right `state:read` and answers
`{"port":9443,"protocol":"tcp","state":"closed","engine":"ufw"}`; `state` is `open`, `closed` or `unknown`.

## When it happens

The ports are settled whenever an endpoint, the settings of the panel or the subscriptions are
saved, applied, opened or removed, and once more when the server starts. A host whose firewall was reset carries the
rules again as soon as the panel comes up.
