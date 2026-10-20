# {{ page.title }}

A channel is a way by which traffic leaves the host: through the host itself, through an AmneziaWG tunnel the host keeps as a client of another server, or through the same tunnel inside WebSocket to a `wstunnel` proxy. Each channel has its own mark: a packet with the mark leaves through this channel. A group gathers several channels under one name. A rule names a channel or a group in the "Channel" field.

{% include shot.html name="channels" alt="List of channels: the direct channel through the host, ready" caption="The direct channel through the host." %}

The "Channels" page is split into "Channels" and "Groups". The list of channels names the type, the server, "fwmark / table", the status ("ready", "not up", "refused" with the reason, "no handshake yet", "the check does not get through", "off") and the traffic both ways; a channel through the host has no traffic, because it has no interface of its own. Channels are moved with the arrows, and "Apply" puts all the channels and rules on the host again. A fresh panel starts with one channel through the host named `direct`.

## Channel

### Types

| Type | What it carries |
|---|---|
| Through the host | the way out of the host itself, without an interface of its own |
| AmneziaWG | an AmneziaWG interface the host raises as a client of another server |
| WebSocket | the same interface, whose UDP goes inside WebSocket to a `wstunnel` proxy |

### Fields

| Field | What it sets |
|---|---|
| Enabled | whether the channel works; the rules through a channel that is off hold their traffic |
| Name | the name of the interface, up to 15 characters; a group cannot take the name of a channel and the other way round |
| Address, Port | where to reach the server; for WebSocket, the server behind the proxy and the port the proxy hands the tunnel to |
| Websocket proxy | the proxy as `proxy.example.net` or `wss://user:secret@proxy.example.net:8443/token`; a bare name takes the port of the server, an address carries the port, the path and the credentials |
| Interface address | the ranges the interface carries, as the server handed them out |
| Name servers | servers reachable through the tunnel |
| MTU, Keepalive | the packet size and how often to keep the path open |
| Close private networks | do not let the clients through the tunnel into private networks (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` and others), where the tunnel network of the server and what is behind it live; on for a new channel |
| Reachability check: Name server, Every, seconds | the server to which the channel sends a question over UDP under its mark, and how often (5 to 3600 s); empty means no check |
| Private key, Public key, New keys | the keys of the interface; the public one is computed from the private one and given to the server |
| Server public key | the key the server is known by |
| Preshared key | a key on top of the handshake, if the server asks for one |
| Obfuscation | the AmneziaWG profile, which must match the server exactly |
| Client configuration | a field at the bottom of the form: paste the `.conf` an AmneziaWG server gave a client, and "Read" fills the form with the keys, addresses, obfuscation and the server |

{% include shot.html name="channel-form" alt="Form of a new AmneziaWG channel: name, reachability check, address and port of the server, interface address, MTU, keepalive" caption="An AmneziaWG channel to another server being filled in." %}

A tunnel without obfuscation is plain WireGuard. A WebSocket channel to another AmneziaGeo server is written as `wss://<host>:<services port>/<WebSocket path of the interface>`: without credentials the proxy gets a token from the keys of the channel and the server and accepts any certificate of the front; without a path it takes `v1`, the path of interfaces of earlier releases. Changing the proxy opens a new carrying WebSocket, turning the channel off closes it.

### Reachability check

A question about `example.com` goes to the named name server under the mark of the channel, that is by the same way as the traffic of the rules, and an answer within two seconds means the way is open. Two misses in a row take the channel out of its groups, one answer brings it back. While a channel has not been checked, the age of the handshake counts instead of the check: a channel through the host always carries, a tunnel while its handshake is younger than three minutes. "Check now" in the row menu sends the question now. A check that could not be marked (without `CAP_NET_ADMIN`) leaves the verdict as it was.

A channel "carries" traffic when it is alive and the check gets through; a rule straight into a channel that carries nothing does not go on the host, with the reason "The channel of the rule carries nothing".

### On the host

A channel gets the first free mark from `0xA601` and a routing table of its own from `42601`; a channel through the host looks into the main table. On the host go an `ip rule` with the mark for the table above the main rule, a default route into the interface in that table, an `unreachable` rule for marked packets whose channel is off, gone or without an interface, so that they do not leak through the host, and the table `inet amneziageo_out`: masquerading what leaves through enabled channels, fitting TCP to the size of the path and filtering what comes out of the tunnels. While the interface of a tunnel is down, its table is empty and the check does not get through: the channel stops carrying, a holding rule holds the traffic, a group moves to another channel, and the client gets `network unreachable` instead of a leak under the address of the server.

The server at the other end of a tunnel is trusted with what the host sent into it, and nothing more: from the tunnel only answers to connections the host opened or let through are taken, and a new connection from the tunnel is dropped. "Close private networks" also refuses a client a new connection through the tunnel into private ranges; with the box cleared, a rule that sends such a range into the tunnel brings the client there under the address of the tunnel.

Adding, changing, turning on, turning off and removing go on the host at once. A channel a rule, a group or the resolver leaves through is not removed: "A rule, a group or the resolver leaves through the channel". The panel watches the interfaces of channels as it watches interfaces: one taken down it raises, one that vanished it puts again with its keys, addresses, route and carrying WebSocket; `ip rule` rules dropped by a restart of a network manager (`systemd-networkd` with `ManageForeignRoutingPolicyRules=yes`) it puts back and writes so in the log. Raising an interface and routing rules need `CAP_NET_ADMIN`; without it the panel answers with a channel with a reason, not with a failed request.

## Groups

A group is several channels under one name; a rule names it the same way as a channel, and the traffic leaves through one of the members.

| Field | What it sets |
|---|---|
| Enabled | whether the group works; the rules through a group that is off hold their traffic |
| Name | up to 32 characters, in one list of names with the channels |
| Strategy | "Priority": the first live channel of the list, and when it falls silent the next one; "Round": new connections over the live channels in turn; "By client": a channel by the address of the client, so one client keeps one channel until the set of live ones changes |
| Channels | up to 16 channels in the order of choice |

Live members are marked "on the host". A group reads the live ones every fifteen seconds and, when the set changes, puts the whole rule set again, so a channel that fell silent is replaced without anyone's help; a change in the panel does not wait for this round. A rule through a group without live members holds its traffic, with the reason "No channel of the group answers", and through a group that is off, "The group is off": the traffic of the rule is dropped until a channel carries again. A group a rule or the resolver leaves through is not removed: "A rule or the resolver leaves through the group". The scenario: [Channel group with a fallback](../recipes/channel-group.html).

## Refusals

| Message | What stands behind it |
|---|---|
| The name takes lower case letters, digits, dash and underscore, and starts with a letter; The name is already taken | the name of the channel |
| The address is neither an address nor a host name; The port is outside 1 to 65535 | the server |
| The interface address is not an address range; The name server is not an address; MTU is outside 576 to 9000; Keepalive is outside 0 to 65535 | network |
| The private key is not 32 bytes in base64; The public key of the server is not 32 bytes in base64; The preshared key is not 32 bytes in base64 | keys |
| The configuration carries no key, no server, or nothing at all | the client configuration did not read |
| The mark is outside the range the panel holds; The routing table does not go with the mark; The panel holds as many channels as it can | marks and tables |
| A rule, a group or the resolver leaves through the channel | the channel is not removed |
| The name of the group does not hold; There is no such strategy; No channel is picked; More channels than a group takes; One channel is named twice; A rule or the resolver leaves through the group | the group |
| The host refused the change | `ip` or `nft` did not take a rule, the reason is in the log |
