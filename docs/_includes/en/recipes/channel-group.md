# {{ page.title }}

The task: the traffic of a rule leaves through one of several servers, and when one fails, the rule moves to another by itself. Several channels are gathered into a group, and the rule names the group.

## 1. Channels

Make two or more channels, as in the recipe [Exit through another server](another-server.html), for example `awg-de` and `awg-nl`, each with its own reachability check: a name server and a period. Without a check a channel counts as alive by the age of its handshake, with it by the answer to a question through the tunnel, and two misses in a row take the channel out of the group, one answer brings it back.

## 2. Group

"Routing" > "Channels" > "Groups" > "Add":

1. "Name" `europe`; the name cannot be the same as the name of a channel.
2. "Strategy":
   - "Priority": the first live channel of the list, and when it falls silent the next one; a main one and a spare;
   - "Round": new connections over the live channels in turn; spreading the load;
   - "By client": a channel by the address of the client: one client keeps one channel until the set of live ones changes; for services that care that the sessions of one person come from one address.
3. "Channels": `awg-de`, `awg-nl` in the order of choice.
4. "Enabled", "Save".

In the list of the group the live members are marked "on the host".

## 3. Rule

In a "Through a channel" rule choose the group `europe` in the "Channel" field; in the list of rules such a channel is marked "group". "Hold the traffic while the channel is down" decides what to do when no member carries: checked, drop; cleared, let out through the host.

## What happens on a failure

The group reads its live members every fifteen seconds, and when the set changes, the rule set goes on the host again: the traffic of the rule moves to the next channel without anyone's help. A connection opened through the failed channel does not move: the decision was made on its first packet, and the client opens a new one. When no member carries, the rule shows "traffic held" with "No channel of the group answers". A change in the panel does not wait for the round: a channel turned off or taken out of the group leaves the set at once.

The resolver can ask through a group too: "Ask through the channel" on the DNS page takes the first live member of a "Priority" group and hands out the questions in turn for a "Round" group.

## Check

"Routing" > "Test" with a name from the rule shows "Through europe" and the members of the group with the marks "carries", "spare", "down", "off" and which one is picked. Take one channel down (turn it off with its switch) and repeat: another member is picked.
