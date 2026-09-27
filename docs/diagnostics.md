# Diagnostics

The **Diagnostics** tab of **Settings** shows what the panel runs on and the latest records of the log of the
panel.

| Route | Right | Returns |
|---|---|---|
| `GET /api/diagnostics` | `access:write` | the runtime, the system, the kernel release and what `wstunnel --version` prints |
| `GET /api/diagnostics/log` | `access:write` | the latest 500 records of the log, the newest first |

The versions of the panel and of the AmneziaWG module come from `/api/health` and `/api/overview`.

The journal keeps what passes the `Logging` levels of `appsettings.json`: `Information` by default,
`Warning` for ASP.NET Core and Entity Framework. It lives in memory and starts empty after a restart;
journald keeps the whole log of the service.

## The connection log

The connection log keeps one record for every new connection of a client: when it was decided, the client
and the address it came from, the interface it came in on, the protocol, the address and the port it went to,
the name the resolver of the panel answered that address for, how it was carried, whether it left the host
itself or was handed to another server, and what came back to it. It is off until it is turned on.

| Route | Right | Does |
|---|---|---|
| `GET /api/access` | `access:write` | the settings, whether the log runs, why not, the records lost, how many records the log holds, over what time and in how many bytes |
| `PUT /api/access` | `access:write` | takes `{"isEnabled": true, "days": 7}`; a field left out stays as it is |
| `GET /api/access/records` | `access:write` | one page of records, the newest first |
| `GET /api/access/summary` | `access:write` | what the records came to, put together one way |
| `DELETE /api/access/records` | `access:write` | removes every record |

`records` and `summary` take `from` and `to` (ISO 8601; the last hour when left out), `client` (a client or a
source address), `verdict`, `way` (an outbound), `path`, `outcome` (one outcome, or `failed` for every outcome
but `ok`) and `search` (a part of the name, the address, the rule or the client). `records` also takes `limit`
(200 by default, 1000 at most) and `before`, the number of the record the page ends before. `summary` takes
`by`: `domain` (the default, the last two labels of the name, three under a country with second level domains),
`name`, `rule`, `way` or `client`; it returns the total, the counts by verdict, by outbound, by path and by
outcome, and up to 500 groups, the largest first, each with how many of its records got through (`ok`) and
how many did not (`failed`).

| Verdict | Means |
|---|---|
| `out` | sent out through the outbound in `way`; `via` names the balancer that picked it |
| `host` | let out the way the host sends its own: by a direct rule, the direct list, or no rule at all |
| `block` | dropped by a blocking rule or the block list |
| `held` | dropped while the channel of its rule, named in `way`, carries nothing |
| `guard` | dropped by a guard of the resolver, `dot` or `doh` in `way` |

| Path | Means |
|---|---|
| `local` | left through the uplink of the host: no rule, a direct rule, or an outbound of kind `local` |
| `relay` | handed to another server through the tunnel of an outbound of kind `wg` or `ws` |

| Outcome | Means |
|---|---|
| `ok` | the other side answered: with data over TCP, with any packet over the other protocols |
| `empty` | the other side took the TCP connection and sent nothing in 10 seconds |
| `reset` | the other side reset the TCP connection, or closed it before it sent anything |
| `silent` | nothing came back in time: 10 seconds after the last packet that asked, 30 at most |
| `unreachable` | an ICMP error came back: the address or the port cannot be reached |

A connection the panel dropped (`block`, `held`, `guard`) has no path and no outcome, and neither has one the
panel stopped watching because the log was turned off or the panel stopped.

### How the records are taken

The kernel decides a connection once, on its first packet, in the chain `decide` of the table
`inet amneziageo_rt` (see [rules.md](rules.md)). While the log runs, every line of that chain hands the packet it
decides to log group 7317 before its verdict, under a prefix that names the rule and the verdict (`ag:o:12`,
`ag:b:-1`, `ag:g:dot`), and a last line hands over what no rule took (`ag:n`). The panel takes the group over
netlink (NFLOG) with the first 96 bytes of each packet. The kernel loads `nfnetlink_log` and `nft_log` itself
when the group is taken and when the ruleset first names it. A host that cannot take the ruleset with the log
gets it without, and the log says why.

The log also watches what comes back. While it runs, `prerouting` puts bit `0x00010000` on the connection mark
of every connection it lets out, and the chain `answer` on the postrouting hook, which the answers the host
passes on and the ones it gives itself both go through, hands to the same group, under `ag:r`, the first packet
that comes back to such a connection and, over TCP, the first one that carries data or resets or closes it; bits `0x00020000` and `0x00040000` note what went, so no other packet of the connection is
handed over. An error about MTU (`fragmentation needed`, `packet too big`) is not an answer. The mark that sends
a connection to its outbound lives in the low 16 bits of the connection mark and is taken back masked
(`ct mark and 0x0000ffff`), so the bits of the log never reach the routing rules. The panel holds a record
until its outcome is known and writes it then, in the place its time puts it.

A connection nothing came back to yet asks again: a repeated SYN, a UDP packet sent once more, a packet the panel
dropped sent again. The kernel decides each of them anew, and the panel folds them into the one record of the
connection instead of making a new one: it waits 10 seconds after the last of them, or after the other side took
the connection, 30 seconds at most from the first packet, and a connection that goes on asking the same way
keeps its one record for 3 minutes.

Nothing waits on the log. NFLOG does not hold a packet: the kernel copies its head to the socket and lets it go
on, and when the socket is full it drops the copy. The panel reads the socket on a thread of its own and hands
the records to the writer through a bounded queue that never blocks: what does not fit is dropped and counted
in `lost`. The writer puts the names on the records and writes them in one step a second. At most 65536
connections wait for their outcome; past that a record is written at once without one. The resolver keeps
the names it answered with in memory while the log runs, and forgets them when it stops.

The records live in `access.db` beside the database of the panel, not in the database itself, and are not
copied by the backups. A record is kept for `days` days (7 by default, 1 to 90), and the log keeps at most one
million records, the oldest going first. The questions to the resolver of the panel (port 53 while the resolver
runs) are left out: they are answered by the panel, and the names they bring are on the records of the
connections that follow.
