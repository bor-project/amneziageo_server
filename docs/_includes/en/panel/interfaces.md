# {{ page.title }}

An interface is the server side of an AmneziaWG tunnel: the UDP port the clients come to, the keys, the addresses of the tunnel network and all the obfuscation of profile 3.1. In the console menu and the utility it is called an endpoint (`endpoint`). The panel keeps the settings in its database and puts the interface into the kernel itself: the key, port and obfuscation into the module, the addresses onto the interface, NAT and closed networks into the firewall. A host that raised its interfaces with `awg-quick` hands them over to the panel whole, see [Moving from awg-quick](#moving-from-awg-quick).

## List

The list names the name, the host, the connection address `<host>:<port>`, the number of clients and the state. Under the address of an enabled interface the closed ports are named if the firewall of the host does not let in its UDP port or TCP services port: "51820 is closed in the firewall of the host" or "51820 and 8446 are closed in the firewall of the host", with the "Open" button when the panel can change the firewall. The name leads to the settings of the interface, "Add" opens the form of a new one. The "default" badge at the name marks the interface a new client goes on when no interface is named.

{% include shot.html name="interfaces" alt="List of interfaces: two interfaces with the connection address, the interface address and the number of clients" caption="Two interfaces of one server." %}

## Form

A new interface comes as a draft that already fits together: port 51820 or the first free one above it, the network `10.8.0.1/24`, MTU 1420, keepalive 25, DNS `1.1.1.1` and `1.0.0.1`, AllowedIPs `0.0.0.0/0` and `::/0`, a fresh key pair and random obfuscation. The name is the first free one like `awgN`; the host is the one another interface names, otherwise the domain of the certificate of the panel, otherwise the address the request came to if the clients reach it from outside, otherwise the public IPv4 of the host. The draft is not written until the form is saved.

{% include shot.html name="interface-form" alt="Form of an interface: name, host, port, interface address, NAT, access to the clients, WebSocket proxy" caption="An interface on port 443 with WebSocket." %}

### General

| Field | What it sets |
|---|---|
| Name | the name of the interface in the kernel, up to 15 characters: lower case letters, digits, dash and underscore, starting with a letter |
| Host | the address or name the clients come to: the `Endpoint` line in the client files. An enabled interface without a host is not saved; a disabled one may lack it |
| Port | the UDP port of the tunnel, each interface has its own. A port closed by the firewall is named under the field with the "Open" or "Open on save" button |
| Services port | the TCP port on which hello, the speed test and WebSocket answer, see [Services on the TCP port](#services-on-the-tcp-port); empty means the number of the UDP port. Several interfaces share one services port |
| WebSocket proxy | whether to carry the tunnel inside WebSocket on the services port. If it did not start, the panel clears the box and writes the reason |
| WebSocket path | the part of the address under which WebSocket comes: letters, digits, `-` and `_`, up to 64 characters; a new interface gets 16 made-up characters, interfaces of earlier releases carry `v1` |
| MTU | the packet size for the clients, 576 to 9000; 0 leaves it to the system |
| Interface address | the networks the interface carries, for example `10.8.0.1/24`; the clients get their addresses from them |
| Closed to clients | networks the clients of the interface are not let into, for example `192.168.0.0/16` of the home network of the server |
| Enabled | whether the interface goes on the host. Disabling takes it off the host, and the clients lose the connection |
| Default | the interface a new client goes on: the form of a new client picks it at once, and a client from the API without `configId` goes on it. Exactly one is always marked: the first interface is marked by itself, marking another takes the mark from the old one, a removed one hands it to the next in order. The mark comes off only by marking another interface |
| NAT for clients | whether to mask what the clients send out with the address of the host; without NAT the clients reach the internet only with routes to their network |
| Access to the clients | who in the tunnel may start a connection to the clients of the interface: "Closed", nobody; "The server alone", the panel and the host; "The whole tunnel network", all the clients of the interface; a single client may choose its own, see [Access to the client](clients.html#access-to-the-client) |
| Client online for, s | for how many seconds after its last packet, keepalive included, a client counts as online: 60 by default, from 10 to 3600 and no less than two keepalive intervals. The "online" mark in the list of clients and the guard against a second device go by it |

### Network

| Field | What it sets |
|---|---|
| AllowedIPs | the networks the client sends into the tunnel: `0.0.0.0/0` and `::/0` is everything. The template of a client replaces this with its own list, see [Templates](templates.html) |
| DNS | name servers for the client, if the resolver of the panel does not answer inside the tunnel, see [DNS](dns.html) |
| Keepalive | how often the client sends an empty packet, in seconds; 0 turns it off |

### Keys

"Private key" is the key of the interface; "Public key" is computed from it and goes to the clients; "New pair" makes new ones. "Preshared key" is added to the handshake, "Generate" makes 32 random bytes; empty means no preshared key. A client without a preshared key of its own takes the key of the interface. The private and preshared keys are given only to a role with the "Manage interfaces" permission; the others get them empty.

### Obfuscation

The fields of the AmneziaWG 3.1 profile: Jc (the number of junk packets), Jmin and Jmax (their size), S1 to S4 (junk before the initiation, the response, the cookie and the data packet), H1 to H4 (magic headers: one number or a range `194488238-194553774`; empty means the classic 1/2/3/4), I1 to I5 (signature packets, up to 1024 characters). Under "Advanced": the header protection key (32 bytes in base64; with it S1 to S4 are no less than 12 bytes), "ContentPaddingAddition", "RekeyAfterTime", "RekeyTimeout", "RejectAfterTime", "KeepaliveTimeout", "MaxHandshakeAttempts" (a number or a range; empty means the WireGuard default), "RandomTrailers" and "DisableCookies" (both ends on AmneziaWG 3.1 or newer). A range is written as two numbers with a dash, and the kernel takes a random value from it. The panel refuses `S1 + 56 = S2`: the initiation and the response would be of one size.

The clients carry the same lines as the interface, so changing the obfuscation of a working interface leaves them without a connection until they get a new configuration: the form warns "Clients stop connecting until they get a new configuration." An interface with other obfuscation is put into the kernel anew: the module does not clear the obfuscation fields and takes some of the changes not in place; the clients go on top and shake hands again.

### Interface file

The "Interface file" field at the bottom of the form of a new interface takes the text of an `awg-quick` file, and the "Read" button fills the form from it: the private key, the addresses, the port, the packet size and the obfuscation come from the file, the rest from the draft. Saving is a plain add, with the same checks. The peers of the same file become clients through the [import](clients.html#import) in the console or the API.

## What goes on the host

An enabled interface goes on the host whole: the interface is added if it is not there, the key, port and obfuscation go into the module, the addresses go onto the interface, it comes up with the MTU set, and its clients go on top. Disabling and removing take the interface off the host. Packet forwarding in both families is turned on at every apply, so a rebooted host gets it again as soon as the server starts.

The rules live in the nftables table `inet amneziageo_in`, which is rewritten at every change of an interface: masquerading the clients behind the address of the host (NAT), letting through the answers to what a client sent, access from the tunnel to the clients that allow it, and refusing the clients the closed networks. An interface that is already up keeps its peers and addresses: only what differs changes.

The panel watches the interfaces: after a kernel message about a change of the interfaces of the host and every five seconds without one. An interface taken down behind its back comes up again, one that vanished is put again with its keys, addresses and clients, and the log says so. A host that refuses is named in the log once, and the interface is tried again once a minute.

Reading and writing an interface need `CAP_NET_ADMIN`; without the right the panel keeps the interface and the clients and writes that the interface refused.

## Services on the TCP port

Besides the UDP port, an enabled interface has a TCP port: the services port. On it the panel answers the AmneziaGeo client app that proves the keys of its file:

| Service | What it does |
|---|---|
| hello | tells the app what the server offers it: WebSocket with its port and path, access from the tunnel, the disconnect signal, the speed test, the subscription address, the geo sources of the panel and the routing presets of the template |
| speed | sends and takes up to 100 MiB on a ticket from hello, so that the app measures the speed to the server inside and outside the tunnel |
| WebSocket | carries the tunnel inside WebSocket under the path of the interface, while "WebSocket proxy" is on |
| subscription | the subscription address of the client, see [Subscriptions](subscriptions.html) |

The port answers over TLS: under the certificate of the panel when there is one, otherwise under a certificate the panel makes itself at the start; the app does not check it, the client is proven by the signature of its keys. If the services port is moved, the file of the client carries the line `# AmneziaGeo Services = <port>`, which other apps read as a comment. The panel can answer on this same port itself: it is enough to name it on the [Server](server.html) tab, and the server opens one TCP port to the world.

For WebSocket the panel runs one `wstunnel` per interface on the loopback, with port `61000 + id % 4000`, and the target list of the front lets it only to the UDP port of its interface.{% if page.kind == "docker" %} The panel starts the front itself and restarts it three seconds after a crash.{% else %} The front is the service `amneziageo-proxy@<interface>` with its arguments in `/etc/amneziageo-server/proxy-<interface>.env` and the target list in `proxy-<interface>.yaml`.{% endif %} The WebSocket path cannot be the same as the path of the panel, of subscriptions or of the WebSocket of another interface on the same port: such a save answers "The path is taken on this port". If the front did not come up or another program holds the services port, the panel clears the "WebSocket proxy" box and names the reason; a front that fell over later and a closed port show in [Services](overview.html#services).

The disconnect signal: disabling a client tells its app to take the tunnel down, see [Disabling a client](clients.html#disabling-a-client).

## Ports in the firewall

The panel keeps ports open only where it was asked to; by default it leaves the firewall of the host alone. The ports of an interface (the UDP port, the TCP services port, both directions of forwarding through the interface and the port of the resolver on the interface while the resolver answers at its address), the port of the panel and the subscription port are turned on with item 23 of the [menu](../menu.html), the commands `amneziageo-server endpoint open <name>`, `panel set --opened on`, `subscriptions set --opened on`, or the "Open" button next to a closed port in the panel. The panel turns the switch on and never turns it off by itself; saving a form without a word about the port leaves the switch as it is.

Where the host has ufw, the panel gives it rules with a mark in the comment by which it knows its own:

```
ufw allow 51820/udp comment 'amneziageo awg0'
ufw allow 51820/tcp comment 'amneziageo awg0'
ufw route allow in on awg0 comment 'amneziageo awg0'
ufw route allow out on awg0 comment 'amneziageo awg0'
ufw allow 8443/tcp comment 'amneziageo panel'
```

Before each change it reads `ufw show added`, adds what is missing and removes only its own rules that nobody needs any more; a rule written by hand stays. A ufw that is off takes the rules and holds them until it is turned on, so `ufw enable` on a working server does not break the tunnels; the panel does not undertake turning ufw on and off. Where there is no ufw, the panel puts the table `inet amneziageo_open` with the same ports and interfaces: on a host that closes nothing this changes nothing, and on a host with ports closed in a table of its own they are opened there by hand. The port of the resolver on an interface whose ports the panel does not hold is let in to ufw by sub-item 13 of menu item 23 and by `amneziageo-server dns open`, see [DNS](dns.html#how-it-works).
{% if page.kind == "docker" %}
The image has no ufw, so the panel in the container puts its own nftables table. On a host with ufw on, its `drop` is stronger: the panel calls such a port closed with the hint "Port <port> is closed by the server firewall (ufw), which the panel in its container does not reach. Open it on the server: sudo ufw allow <port>", and the `amneziageo-server` menu on the host opens it with item 23.
{% endif %}
The form names a closed port under the field. If the panel can change the firewall and the role has the permission for the section (interfaces: "Manage interfaces"; the panel and subscriptions: "Manage access"), a button stands next to it: "Open" for a saved port keeps it open at once; "Open on save" for a new or moved port opens it together with the save, "Do not open" cancels that. A new port on the "Server" and "Subscriptions" tabs that the firewall closes is not saved until it is opened, so that the panel does not move to where nobody reaches it; an interface is saved anyway. When the firewall refused, the form writes "The firewall of the host refused the port", and the switch stays on until the next apply.

A port the panel kept open that moved to another number is opened on the new one and closed on the old one. With ufw the form asks "Close the former port 51820/udp?": "Close" removes the old rule, "Leave open" hands it over to the host under the comment `kept by amneziageo <name>`, and the panel does not touch it after that (the hint: "A port left open stays open in the server firewall (ufw) as a rule of its own, which the panel no longer touches."). The `amneziageo_open` table keeps nothing, so without ufw the old port is closed without a question.

The panel reads the state of a port from `ufw status verbose`: the first rule that names the port decides, otherwise the policy for incoming traffic. Where there is no ufw, it reads the chains ufw leaves in nftables. A host with neither says nothing, and the port is not named. The ports are put again at every save, apply, opening and removal of an interface, of the settings of the panel and of subscriptions, at a restart of the resolver and at the start of the server, so a host with a reset firewall gets the rules back as soon as the panel is up.

## Moving from awg-quick

A host that raised its interfaces with `awg-quick` hands the interfaces and the clients over to the panel from the files:

```
amneziageo-server import endpoint /etc/amnezia/amneziawg/awg1.conf --host vpn.example.com
amneziageo-server import peers /etc/amnezia/amneziawg/awg1.conf --endpoint awg1
amneziageo-server import clients /root/awg-clients.json --endpoint awg1 --v6 <prefix>
```

`import endpoint` reads the interface file into an interface of the panel with its keys and obfuscation and names the address for the clients. `import peers` reads the peers of the same file: the names come from the comments above them, and there are no private keys there, so such a client is managed, but the panel will not hand it a file, and a client that needs a file gets a new one. `import clients` reads a file in which the host kept its clients with private keys; `--v6` adds to each an address of the second family by the last number of the first. A client whose public key the panel already knows is skipped, so the import can run twice without doubling; a name that is taken gets a number (`anna-2`). Menu item 25 does the same with questions. In the panel, of all this there is the "Interface file" field of the form of a new interface; clients from text are taken by `POST /api/clients/import`, see [Import](clients.html#import).

Then stop the old services and start the panel:

```
systemctl disable --now awg-quick@awg0 awg-quick@awg1
systemctl start amneziageo-server
```

The interfaces keep the keys, ports, addresses and obfuscation from the files, and the clients come back by themselves with the files they already hold. What the old `PostUp` dropped is written into "Closed to clients", what it masqueraded is "NAT for clients". The old files stay in place, and `systemctl enable --now awg-quick@awg0` takes the host back while the service of the panel is stopped. Everything that managed the peers before the panel (sync scripts, timers) must stop, or the two will undo each other. A copy of the files of the host and of the database of the panel `/var/lib/amneziageo-server/server.db` is worth keeping before the move.

The panel adds and changes the peers of an interface and removes those it disabled or removed; a peer it does not know is left in place, and `POST /api/clients/apply` lists such peers as extra. The same clients are written into the interface file in `/etc/amnezia/amneziawg`, so that a reboot does not lose them; the head of the file before the first `[Peer]` is kept as it is. `Endpoints__Directory` sets the directory, `Endpoints__KeepFile=false` turns writing the file off.

## Removing

Removing stands on the page of the interface: "Remove <name>?". It takes the interface off the host together with its clients. An interface named by routing rules stays in them by name: such a rule does not go on the host, with the reason "The panel holds none of the interfaces the rule names".

## Refusals

| Message | What stands behind it |
|---|---|
| The name takes lower case letters, digits, dash and underscore, and starts with a letter | the name is empty, longer than 15 characters or has other characters |
| The address is neither an address nor a host name; Name the host the clients reach the server at | the host did not parse; an enabled interface without a host |
| The port is outside 1 to 65535; The port is already taken by another interface | the UDP port |
| The interface address is not an address range; AllowedIPs is not an address range; The name server is not an address; A closed range is not an address range | networks and addresses |
| MTU is outside 576 to 9000; Keepalive is outside 0 to 65535; At least two keepalive intervals, within 10-3600 s | sizes and times |
| The private key is not 32 bytes in base64; The preshared key is not 32 bytes in base64; The header protection key is not 32 bytes in base64 | keys |
| The junk sizes do not hold together; The packet types are the same or below five; A special junk packet is longer than 1024 characters; The value takes one number or a pair separated by a dash; The header protection key needs S1 to S4 of at least 12 bytes | obfuscation |
| The name is already taken | an interface with this name exists |
| The path takes letters, digits, - and _ in one part; The path is taken on this port | the WebSocket path |
| The configuration carries no key, no server, or nothing at all | the interface file did not read |
| The kernel has no amneziawg module; The panel lacks the rights to change the host network; The host keeps packet forwarding off; The interface did not come up: <reason>; The WebSocket proxy did not start: <reason> | the host did not take the interface |
| The firewall of the host refused the port | ufw or nft refused to open the port |
