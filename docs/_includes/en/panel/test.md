# {{ page.title }}

The route test tells what the rules on the host will do with a connection. The page opens at `/routing/test` and by a link from the routing pages; it is not in the menu.

| Field | What it sets |
|---|---|
| Name or address | a name, an address or a link whose host is taken; the name is asked through the way out of the resolver, and with the resolver off through the host |
| Port | the destination port; may be left empty |
| Client | a client of the panel whose addresses and interface the connection carries, or an address whose interface is the interface of its network |
| Protocol | TCP or UDP |
| Source port | may be left empty |

"Test" reads the rules the way the set on the host reads them. First comes the guard of the resolver, while at least one rule on the host compares by name: with DNS over TLS forbidden, port 853 is dropped, with DNS over HTTPS forbidden, port 443 to known name servers. Then the resolver itself: while it intercepts queries, it answers every question to port 53 from the interface of a client, and the rules are not read. Then the rules in order: a rule not on the host is skipped with its reason, a rule whose interface, client, protocol, port or source port did not match is missed, and a rule whose targets the traffic falls under matches by range, by name or by an address the resolver put into its set.

## Outcome

| Outcome | What it means |
|---|---|
| Through <name> | the traffic leaves through the channel; for a group its channels are listed with the marks "carries", "spare", "down", "off" and which one is picked for this rule |
| Direct through the host | a "Direct" rule, the "Direct" list, or no rule matched |
| Blocked | a "Block" rule or the "Block" list |
| Held: <name> carries nothing | the rule matched, but its channel carries nothing, and the rule holds the traffic |
| Dropped by the resolver guard (dot or doh) | a connection to a name server past the resolver |
| Answered by the resolver of the panel | a DNS question the resolver intercepts |

Below stand the "Rule" that decided, with its place, the "Addresses" the name resolved to ("the name did not resolve"), and the list of rules read before the decision, each with "By what": "matched", "missed" with what did not match (target, interface, client, protocol, port, source port), or "not on the host" with the reason. This shows which rule caught the traffic earlier than intended and why the right one missed.
