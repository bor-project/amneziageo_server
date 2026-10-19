# {{ page.title }}

"Overview" is the first page of the panel. It reads the host every two seconds and shows its state, the services of the server, the update of the panel and the backup.

{% include shot.html name="overview" alt="Overview page: the AmneziaWG module, services, CPU, memory, swap and disk cards, speed and connections" caption="The overview of a working server." %}

## Page header

Under the title stand `panel <version>`, the AmneziaWG module (`loaded <version>` or `not loaded`) and `interfaces N`: how many interfaces of the `amneziawg` type are up on the host. When a release newer than the panel is out, `<version> available` appears next to them with a link to its page, and the "Update" button, see [Updates](#updates); "Check for updates" asks for the releases now.

## Cards

| Card | What it shows |
|---|---|
| CPU | the load, the model, `N cores / NT · clock`; under the chart "AVG" and "PEAK" over the window |
| RAM | the memory in use, under it the average and the peak |
| Swap | the swap in use |
| Storage | what is taken on the disk where the root lies, "FREE" |
| Overall speed | receiving and sending over all the interfaces of the host except the loopback, the peak over the window, the total sent and received, the average over the window |
| Connections | open TCP and UDP sockets |
| Uptime | of the system and of the panel |
| Process | the memory and threads of the panel |
| Addresses | the addresses of all the interfaces up on the host |

The numbers come from `/proc` of the host without kernel privileges. The window of the charts is the last 120 samples, four minutes; the kernel counters reset with a reboot of the host, so the first sample after it gives no speed.

## Services

The "Services" card lists what the server keeps for the clients and the ports of each service, with the mark "checked <time>":

- "Panel": the TCP port the panel answers on;
- "Subscriptions": on their own port or "on the ports of the interfaces", while subscriptions are on;
- "Interface <name>" for each enabled interface, with the parts its TCP port serves: hello, speed, WebSocket, subscription;
- "DNS": the resolver, while it is on;
- "Certificate <name>" for each certificate file the panel and subscriptions answer under, with "until <date>".

The "Change" link in a row leads to where the port and the settings of the service change: the panel - [Server](server.html), the subscriptions - [Subscriptions](subscriptions.html), an interface - [its page](interfaces.html), DNS - [DNS](dns.html), a certificate - [Certificates](certificates.html). A user who may change those settings sees it.

A service that is down carries "Down since <time>: <reason>":

| Reason | What it means |
|---|---|
| no host is set | the interface has no host, and the files of its clients carry no `Endpoint` |
| the interface is not on the host | the interface is not up; with "the AmneziaWG module is not loaded" when the module is the reason |
| the host did not give the port | the TCP services port did not open, another program holds it |
| the firewall of the host keeps `<port>` closed | the firewall does not let in the UDP or TCP port, see [Ports in the firewall](interfaces.html#ports-in-the-firewall) |
| the WebSocket front does not run, fell over, does not listen | the WebSocket proxy of the interface does not work, fell over since the last check or does not listen on its port on the loopback |
| no interface hands them out | subscriptions are on the ports of the interfaces, and no port answers |
| the resolver does not run, the way out of the resolver is broken | the resolver did not come up or no upstream server answers |
| The file does not read, Ran out on <date> | the certificate file does not read or the certificate has run out |
| Runs out in N d. | the certificate runs out in less than 14 days; the service works, this is a warning |

The check runs five seconds after the server starts and every 30 seconds after that; the firewall is read no more often than once in five minutes, and at once for a new port. A firewall outside the host, at the hoster, is not visible to the panel. While at least one service is down, the header of every page says "Services down: N" and leads here. The log of the panel gets the lines `the service <name> is down: <reasons>` and `the service <name> works again`, and a line for the console menu, like `all 3 work`, lies in the `services` file next to the database and shows in the status under the [menu](../menu.html).

## Updates

A minute after the start and every 12 hours the panel asks GitHub about the releases of `bor-project/amneziageo_server`: the latest release, and with pre-releases the five newest. It takes the newest of its channel that has an `update.json` manifest and an `update.json.sig` signature, checks the signature with the key it carries inside and offers the release if it is newer than itself. A manifest with a bad signature is not offered, and the panel names the reason. A request without an answer is repeated up to five times within a minute, after which the header says "updates not checked", and the next check starts over.

The "Update" button, for a role with the "Update the panel" permission, leads through the steps "Download", "Install", "Restart", "Done" with notes: "downloading <version>", "putting the release on", "the panel restarts, waiting for it", "the panel runs <version>, the page reloads itself". The page waits for the new panel to answer and loads it; if there is no answer for long, it says "the panel has not answered for N min". A failed update, one whose script ended with an error or that left the panel on its former version, stays in the header as "the update to <version> failed" with its log until the next update goes through or a role with the "Update the panel" permission hides it with the "Hide" cross next to it; once hidden, it is hidden for every account. An update neither of whose two versions the panel runs any more (it was put on another way: the menu, `install.sh`, another image tag) is not shown in the header. The `update` directory keeps the logs and exit codes of the last five updates; older ones go at the next look at the releases.
{% if page.kind == "docker" %}
The container updates through the Docker socket its `compose.yaml` gives it: the panel pulls the image of the release by digest, copies the database to `backup` in the data directory, writes `AMNEZIAGEO_TAG=<version>` into the `.env` of the project, stops itself and starts the helper container `amneziageo-server-update-<version>`, which recreates its container on the new image with the same settings. The old container waits under its own name: if the new one does not become healthy within three minutes, the database and `.env` come back, and the old one goes on working. The helper containers of finished updates are removed at the next check.
{% else %}
The service updates through the `install.sh` of the new release: the panel assembles the archive for the architecture of the host in `update/stage-<version>` next to the database, taking the files it already has from the working release and downloading the rest from the release in parts, each checked against the digest from the file list of the release; between two releases this is usually a megabyte or two out of about 55. If the list does not match, the files are not served in parts or more than three quarters of the archive changed, the panel downloads the whole archive. Then it runs `install.sh` apart from itself, through `systemd-run`, as the service `amneziageo-server-update-<version>`: that stops the server, copies the database, moves `current` to the new release and brings the previous one back if the new one did not come up. The output of the script goes to `update/<version>.log`, the result to `update/<version>.rc`.
{% endif %}
Why the panel cannot update itself, the header says in words:

| Reason | What it means |
|---|---|
| the panel runs neither from the package nor in a container | updates by hand only |
| the package runs under a user other than root | the service runs as another user |
| the host carries no systemd-run | there is nothing to run the update script apart from the panel with |
| the container is not given the Docker socket | `compose.yaml` has no `/var/run/docker.sock` |
| the panel does not find its container; the container was not started by compose; a compose file lies outside the project directory; the data directory is not mounted into the container | the container is not set up the way the update expects |

The release channel is the "Receive prerelease versions" box on the [Server](server.html) tab; the variable `Update__Channel=test` in the environment of the panel takes pre-releases whatever the box says. Other variables: `Update__CheckHours`, how often to look at the releases (12; `0` leaves the check to the button), `Update__Repository`, whose releases to read, `Update__Manifest`, the address of a manifest instead of the releases, `Update__Key`, a file with another public key, `Update__Directory`, the directory of downloads and logs, `Update__Docker`, the socket of the daemon. What happens to interfaces, channels and the resolver during the restart: see [Installation](../install.html#updates).

## Backup

The "Backup" card: "A copy of the database of the panel: interfaces, clients, templates, routing, accounts and settings."

"Download a copy", for a role with the "Download backups" permission, saves the database as it is at that moment as a file `amneziageo-<server name>-<time>.db`: accounts and tokens, interfaces with their keys, clients, rules and the settings of the panel. The copy does not include the certificate of the panel, the token signing key, the geo database files, the event log and the connection log.

"Restore from a file", for a role with the "Manage access" permission, after the question "Restore the panel from a backup?" sends the file (up to 1 GiB, with the counter "Sending the file: N%"), and the panel checks it: that it is a whole database of the panel, not newer than the panel itself, and that with its settings the panel comes up on this host: the addresses it listens on, and the certificate with its key. The accepted file waits next to the database, the panel says "The panel starts over on the backup" and starts again: the former database with its journal goes to `backup/<time>-before-restore` in the data directory, the copy takes its place, a copy from an older release is brought up to the current one at the start, and the page waits until the panel answers again.

| Refusal | What it means |
|---|---|
| The file is empty; The file is not a database; The database is not one of the panel; The database is damaged | the file is not a copy of the panel |
| The backup comes from a newer release of the panel, update the panel first | update the panel and try again |
| The panel would not start on this server with this backup | the copy holds an address the host does not have or a certificate missing on the disk; `amneziageo-server restore <file>` in the console fits such a copy to the host |
| The file is larger than 1 GiB | the file is too large |

A backup moves the panel to another server: `amneziageo-server install --restore <file>` on the new host, see [Moving to another server](../recipes/move.html). The copies taken before updates lie in `backup` in the data directory and come back with menu item 5.
