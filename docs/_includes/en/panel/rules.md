# {{ page.title }}

A rule says where traffic goes: it compares what the client asks for and either sends it through a channel, lets it out through the host, or drops it. The rules lie in the order of reading, and the whole set turns into one nftables rule set the host takes at once.

## List

The list shows the rules in their order with a number, a switch, what the rule compares ("Where to", "Where from", "Ports", "Inbound"), where it sends ("Traffic": a channel or a "group"), how many ranges and names came out ("Ranges"), and the status: "on the host", "off", "traffic held" with the reason, or the reason the rule did not go on the host. A rule is moved with the "Up" and "Down" arrows and by dragging the row, while the list is neither sorted nor filtered. The switch turns a rule on and off; a rule that is off is not in the set.

## Form

### General

{% include shot.html name="rule-form" alt="Form of a new rule: the name youtube, the action Through a channel and the targets of the recipe" caption="A rule being filled in, not saved yet." %}

| Field | What it sets |
|---|---|
| Name | up to 32 characters |
| Action | "Through a channel", "Direct", "Block" |
| Channel | the channel or group through which the traffic of a "Through a channel" rule leaves |
| Protocol | "Any", TCP, UDP |
| Ports | destination ports: `443`, `1000-2000` |
| Source ports | the ports the traffic comes from, the same way |
| Hold the traffic while the channel is down | what to do with the traffic of the rule while its channel carries nothing: checked, drop it; cleared, let it out through the host, see below |
| Enabled | whether the rule goes on the host |

A new rule holds the traffic and is off until turned on.

### Addresses

| Field | What it sets |
|---|---|
| Where to | geo keys, domains and address ranges the traffic goes to |
| Where from | addresses and ranges of the clients the traffic comes from |
| Clients | clients of the panel by name: their addresses are added to "Where from", and the rule is put on the host again at every change of a client, so a new address of the client is picked up by itself |
| Interfaces | interfaces of the panel by name: the rule compares only traffic that came to one of them |

A target is read by its look: `geoip:ru` is a country, `geosite:youtube` a category, `203.0.113.0/24` and `198.51.100.7` ranges, anything else with a dot a domain. A domain is also taken as `domain:example.com` and a range as `cidr:10.0.0.0/8`, in the form of the lists of the AmneziaGeo client, so a list moves from one side to the other without edits. `keyword:ads` fits every name that has the word in it; a keyword has no addresses of its own, and the addresses of such a rule come only from the answers the resolver sees. Ranges come from `geoip:` and networks in the targets, names from `geosite:`, domains and keywords. A rule without targets fits any address, without "Where from" and "Clients" any client, without "Interfaces" any interface.

Renaming a client, interface, channel or group writes the new name into every rule that names it.

## Order

The rules are read top down, and the first that fits decides. The decision is made once, on the first packet of the connection: a "Through a channel" rule puts the mark of the channel, and it stays on the connection; a connection without a mark goes where the host would send it. A connection stays on its path both after the rules change and after its address later gets into the set of another rule. A "Block" rule drops a new connection; one opened before the rule lives until it ends. A "Direct" rule returns from the decision without a mark, and the rules below are not read.

The mark goes back only onto what the clients sent: an answer that comes through a channel carries no mark, and the host finds the client in its tables rather than sending the answer back into the channel.

## What does not go on the host

A rule enters the set only when everything it names is there. Otherwise the list shows the reason:

| Reason | What it means |
|---|---|
| There is no such channel | the rule names a channel the panel does not know |
| The channel of the rule is turned off | the channel is off |
| The channel of the rule carries nothing | the check of the channel does not get through, or the interface of the channel is not up |
| No channel of the group answers | the group has no live channel |
| The geo databases carry nothing for this rule | the databases have nothing of what the rule compares |
| There is no such client | the panel knows none of the clients named |
| The panel holds none of the interfaces the rule names | the panel knows none of the interfaces named |

The first four reasons say that the rule is fine, but its way out is gone. Then "Hold the traffic while the channel is down" decides: a held rule goes on the host anyway, but drops its traffic instead of marking it, and the list writes "traffic held" with the reason; a rule that is not held lets its traffic out through the host, under the address of the server itself, which a client behind a channel usually does not expect. An empty target is a fault of the rule itself, and such a rule simply does not go on.

## On the host

The rules live in the nftables table `inet amneziageo_rt`, which is written whole and loaded in one step. For each rule and family it has a set of ranges `r<id>v4` and `r<id>v6` from the geo databases and a set of names `n<id>v4` and `n<id>v6` with a lifetime, which the resolver fills with addresses from the answers to the names of the rule, see [DNS](dns.html). The `prerouting` chain leaves alone everything that did not come to an interface of the panel, drops a packet of a client without a tracked connection, gives the mark back to a connection that already has one, and passes only a new connection on to the `decide` chain; `decide` carries the basic lists and the rules after them. The whole set is printed by item 23 of the console menu, sub-item 12. The rules are put at the start of the panel, after every change and when the set of live channels changes.

## Refusals

| Message | What stands behind it |
|---|---|
| The name is empty or too long | the name |
| The action is a channel, direct or a block; The rule names no channel | the action |
| One of the targets is not a geo key, a domain or a range; One of the client addresses is not an address or a range | "Where to", "Where from" |
| One of the client names is not a name a client takes; One of the interface names is not a name an interface takes | "Clients", "Interfaces" |
| The protocol is any, TCP or UDP; A source port is not a port or a range of ports | protocol and ports |
| The rule carries too many conditions | the rule does not fit into the set |
| There is no such rule; There is no such channel | a number or name the panel does not know |
