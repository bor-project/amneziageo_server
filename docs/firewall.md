# The ports of the panel in the firewall of the host

The panel writes its own nftables tables, and a table of its own says nothing to the firewall the host
already runs: a `drop` in the chain of ufw ends the packet whatever another table accepts. So a port is
opened where the host closes it, in ufw itself.

Every port stays closed until it is asked for. Each thing that listens carries `Open the port in the
firewall`, off by default: an endpoint, the panel and the subscriptions. Nothing changes on a host
that was set up by hand until a toggle goes on.

## What is opened

| Toggle | What it opens |
|---|---|
| An endpoint, see [configs.md](configs.md) | its UDP port, the TCP port of its services, see [services.md](services.md), and both ways through its interface, so its clients reach the internet and the ports carried to them arrive |
| The panel, see [serving.md](serving.md) | the port the panel binds, unless it binds the loopback alone |
| The subscriptions, see [subscriptions.md](subscriptions.md) | the port they are served on, while they are handed out |

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

## When it happens

The ports are settled whenever an endpoint, the settings of the panel or the subscriptions are
saved, applied or removed, and once more when the server starts. A host whose firewall was reset carries the
rules again as soon as the panel comes up.
