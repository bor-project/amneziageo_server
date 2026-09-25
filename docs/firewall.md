# The ports of the panel in the firewall of the host

The panel writes its own nftables tables, and a table of its own says nothing to the firewall the host
already runs: a `drop` in the chain of ufw ends the packet whatever another table accepts. So a port is
opened where the host closes it, in ufw itself.

Every port stays closed until it is asked for, and nothing is asked for by default. An endpoint carries `Open the
port in the firewall` in the panel. The port of the panel and the port of the subscriptions are held open from the
menu of the server, `amneziageo-server`, item 23 `Firewall Management`, or by
`amneziageo-server panel set --opened on` and `amneziageo-server subscriptions set --opened on`. Nothing changes on a
host that was set up by hand until one of them goes on.

## What is opened

| Toggle | What it opens |
|---|---|
| An endpoint, see [configs.md](configs.md) | its UDP port, the TCP port of its services, see [services.md](services.md), and both ways through its interface, so its clients reach the internet and the ports carried to them arrive |
| The panel, item 23 of the menu, see [serving.md](serving.md) | the port the panel binds, unless it binds the loopback alone |
| The subscriptions, item 23 of the menu, see [subscriptions.md](subscriptions.md) | the port they are served on, while they are handed out on a port of their own |

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
written by hand stays where it is, with or without a comment of its own.

A ufw that is turned off takes the rules all the same and holds them until it is turned on, so `ufw enable`
on a running server does not cut the tunnels off. The panel never turns ufw on or off itself.

Where the host carries no ufw, the panel lays the table `inet amneziageo_open` instead, with the same ports
in its `input` chain and the interfaces in its `forward` chain. On a host that closes nothing this changes
nothing, and on a host closing ports in a table of its own the ports are still to be opened there by hand.

## A closed port in the panel

The tabs `Server` and `Subscriptions` of the settings ask the firewall of the host whether their port is let in, and
name a closed port under the field along with where it is opened: item 23 of the menu. A new port the firewall
closes is not saved until it is opened, so the panel does not move where nobody reaches it; a port that stays as it
was is only named. Where the host carries ufw, the panel reads `ufw status verbose`: the first rule that names the
port decides, the policy for what comes in otherwise. Where it does not, as in a container, the panel reads the
chains ufw leaves in nftables, `ufw-user-input` and the policy of `INPUT`. A host with neither tells nothing, and
nothing is named.

`GET /api/firewall/port?port=<port>`, with `&protocol=udp` for a UDP port, takes the right `state:read` and answers
`{"port":9443,"protocol":"tcp","state":"closed","engine":"ufw"}`; `state` is `open`, `closed` or `unknown`.

## When it happens

The ports are settled whenever an endpoint, the settings of the panel or the subscriptions are
saved, applied or removed, and once more when the server starts. A host whose firewall was reset carries the
rules again as soon as the panel comes up.
