# The overview of the host

`Overview` is the first page of the panel and it reads `GET /api/overview` every two seconds under the
right `state:read`. The answer carries the numbers of the host and the window the graphs are drawn from.

## Where the numbers come from

| Card | Source |
|---|---|
| CPU | the head of `/proc/stat`, the busy share between two readings; the model, the cores and the clock from `/proc/cpuinfo` |
| RAM | `MemTotal` and `MemAvailable` of `/proc/meminfo` |
| Swap | `SwapTotal` and `SwapFree` of `/proc/meminfo` |
| Storage | the drive the root sits on |
| Overall speed | the bytes of every interface but the loopback in `/proc/net/dev`, the growth between two readings |
| Connections | `inuse` of `TCP`, `UDP` in `/proc/net/sockstat` and of `TCP6`, `UDP6` in `/proc/net/sockstat6` |
| Uptime | `/proc/uptime` for the host, the start of the process for the panel |
| Process | the working set and the threads of the panel |
| Addresses | the unicast addresses of every interface that is up |

The head of the page reads `/sys/module/amneziawg`: the version the module reports and the interfaces
whose `uevent` carries `DEVTYPE=amneziawg`. Beside the version of the panel it shows the release newer than the
panel, see [updates.md](updates.md).

The card `Backup` beside the uptime downloads a copy of the database for a role with `backup:read` and restores
the panel from such a file for a role with `access:write`, after a question; see [install.md](install.md).

## The services

`ServiceWatch` checks the services the server runs for its clients five seconds after the start and then every
30 seconds, and keeps what it found. `GET /api/overview/services` reads the last check, `POST /api/overview/services`
checks at once; both take `state:read`. The answer lists what should run:

| Service | Listed when | Down when |
|---|---|---|
| `subscription` | the subscriptions are turned on | on a port of their own: the host refused the port (`port-refused`) or the firewall keeps it closed (`port-closed`); on the ports of the endpoints: none of those ports answers (`no-endpoint`) |
| `endpoint` | the endpoint is turned on | it names no host, so the files of its clients carry no `Endpoint` (`no-host`), its interface is not on the host (`interface-down`, with `no-module` when the module is not loaded), the host refused its TCP port (`port-refused`), its websocket front does not run (`front-down`), fell over since the check before (`front-fell`) or does not listen on its loopback port (`front-deaf`), the firewall keeps its UDP or TCP port closed (`port-closed`) |
| `dns` | the resolver of the clients is turned on | it does not run (`resolver-down`) or its way out is broken (`resolver-way`) |

Each service carries its ports, whether it answers on the port of the panel, what the TCP port of an endpoint
hands out (`hello`, `speed`, `websocket`, `subscription`), the faults with what the host said, and since when it
works or does not. The port of the panel is never counted as refused. The front is read off `systemctl show`
(`ActiveState`, `NRestarts`, `ExecMainStatus`) on a host with systemd and off the process the panel runs
otherwise; whether it listens is read off `/proc/net/tcp` and `/proc/net/tcp6`, so the check never connects to it.
The firewall is read as [firewall.md](firewall.md) reads it, at most once in five minutes and at once when a port
is new to it. The panel cannot see a firewall outside the host, the one of the hoster among them.

A service that stops working writes `the service <name> is down: <faults>` to the log of the panel, one that
works again writes `the service <name> works again`. The line for the menu of the host, like
`2 of 3 down: subscriptions (port-refused), endpoint awg1 (port-closed udp 443)` or `all 3 work`, where a closed port
carries its protocol and number, goes to the file
`services` beside the database whenever it changes; while the panel runs, the status of the menu shows it as
`Services:` and item 18 as `services:`.

## The window

`SystemMonitor` beats every two seconds and keeps the last 120 readings, four minutes of history. A beat
turns two readings into one point: the load of the processor, the memory, the swap and the storage taken,
the bytes a second up and down, the sockets open. `AVG` and `PEAK` under a card are counted over that
window, and so is `peak` beside the speed.

The counters of the kernel restart with the host, so a reading below the one before it gives no rate at
all instead of a negative one.

## The parts

| Where | Holds |
|---|---|
| `AmneziaGeo.Server.Core/Status/ProcText.cs` | the parsers of the /proc text, plain functions over a string |
| `AmneziaGeo.Server.Core/Status/SystemProbe.cs` | the files, the drive, the interfaces and the process |
| `AmneziaGeo.Server.Core/Status/SystemMonitor.cs` | the beat, the window and the answer |
| `AmneziaGeo.Server.Api/Status/OverviewEndpoints.cs` | the routes under `state:read` |
| `AmneziaGeo.Server.Api/Status/ServiceHealth.cs` | the checks of the services, plain functions over what was read |
| `AmneziaGeo.Server.Api/Status/ServiceWatch.cs` | the watch: what it reads off the host, the log and the line for the menu |
| `amneziageo-web/src/pages/Dashboard.tsx` | the page |
| `amneziageo-web/src/components/Chart.tsx` | the graphs, plain SVG |

Reading the host asks for no privilege of the kernel: the panel runs the overview as it runs, and only the
interfaces of the tunnels need `CAP_NET_ADMIN`.
