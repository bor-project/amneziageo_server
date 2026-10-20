# {{ page.title }}

The "Diagnostics" tab shows what the panel runs on, its event log and the connection log of the clients.

{% include shot.html name="diagnostics" alt="Diagnostics: versions of the components and the event log" caption="Versions and the event log." %}

## Versions

"Panel", "AmneziaWG" (the version of the module or "none"), ".NET", "System", "Kernel" and "wstunnel", which is what `wstunnel --version` prints.

## Event log

The last 500 records of the log of the panel, newest on top: the time, the level, the source and the message; "Refresh" reads them again. The records also live in the `journal.db` file next to the database of the panel: a line gets into it within a second, and after a restart the panel reads the last 500 back, so the page shows what happened before the restart as well. The file holds up to a hundred thousand records and 32 MB (`Journal__MostRecords` and `Journal__MostBytes` set other limits, no lower than 1000 records and 1 MB), and the old ones go first. The file is not part of the database backup.

What the process prints goes where the host keeps its logs: {% if page.kind == "docker" %}into the log of the container (`json-file`, 10 MB in 3 files, as `compose.yaml` sets), read with `docker compose logs`{% else %}into journald, read with `journalctl -u amneziageo-server`{% endif %}; item 19 of the console menu shows the same.

## Connection log

The connection log keeps a record of each new connection of a client: when it was decided, the client and the address it came from, the interface, the protocol, the destination address and port, the name the resolver of the panel answered for that address, how the connection was carried, whether it left the host by itself or was handed to another server, and what came back. It is off until turned on.

"Record connections to the log" turns recording on, "Keep" sets how many days to keep the records (7 by default, 1 to 90); the log holds no more than a million records, and the old ones go first. The status line says how many records there are, since when, how many bytes, and "N lost" if records did not fit into the queue. "Clear" removes all the records. The records live in `access.db` next to the database of the panel and are not part of the backup.

### Summary and connections

The filters: "Period" ("Last hour", "Last 6 hours", "Last 24 hours", "Last 7 days", "Custom range" with "From" and "To"), "Client" (a client or a source address, "all"), "How" (locally, relayed, through <channel>, direct, block, held, guard), "Outcome" (success, failure, no answer, reset, no data, unreachable), "Name or address".

"Summary" counts the connections, locally, relayed, blocked and held, success and failure, and groups them with "Group by" by domain (the last two parts of the name, three under a country with second-level domains), name, rule, channel or client: up to 500 of the largest groups with the number of successful and failed ones ("The N largest groups are shown"). "Connections" lists the records: the time, the client, "Where to" (a name or an address with the port), the rule ("none" if no rule matched), "How" and "Outcome"; "Show more" loads further.

| How | What it means |
|---|---|
| through <channel> | sent through a channel; for a group the chosen channel is named |
| direct | let out the way the host sends its own: a "Direct" rule, the "Direct" list, or no rule |
| block | dropped by a "Block" rule or list |
| held | dropped while the channel of the rule carries nothing |
| guard | reset by the guard of the resolver, DNS over TLS or HTTPS |
| locally, relayed | the path: left through the way out of the host, or handed to another server through the tunnel of a channel |

| Outcome | What it means |
|---|---|
| success | the other side answered: with data over TCP, with any packet over other protocols |
| no data | the other side took the TCP connection and sent nothing for 10 seconds |
| reset | the other side reset the TCP connection or closed it without sending anything |
| no answer | nothing came back in time: 10 seconds after the last question packet, 30 at the most |
| unreachable | an ICMP error came: the address or the port is unreachable |

A dropped connection (block, held, guard) has neither a path nor an outcome, and neither has one the panel stopped following because the log was turned off or the panel stopped.

### How the records are taken

The kernel decides a connection once, on its first packet, in the `decide` chain of the rule table. While the log runs, each line of the chain hands the packet to the NFLOG group 7317 before the verdict, under a prefix with the rule and the verdict, and the last line hands over what no rule took; the panel reads the group over netlink with the first 96 bytes of the packet. The log also watches the answer: the `answer` chain hands over the first packet that came back to the connection, and over TCP the first one with data, a reset or a close. Nothing waits for the log: NFLOG does not hold the packet, and what did not fit into the queue of the panel is dropped and counted in "lost". A repeated SYN and a repeated UDP packet fold into one record of the connection. Questions to the resolver of the panel do not go into the log: the names from them stand on the records of the connections that follow them. Connections the panel led itself to read a name, see [Names from connections](dns.html#names-from-connections), are written with the name the connection gave and the outcome the panel saw.

A host that does not take the rule set with the log lines gets it without them, and the log says why. The kernel loads the `nfnetlink_log` and `nft_log` modules by itself.
