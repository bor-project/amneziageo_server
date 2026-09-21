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
| `AmneziaGeo.Server.Api/Status/OverviewEndpoints.cs` | the route under `state:read` |
| `amneziageo-web/src/pages/Dashboard.tsx` | the page |
| `amneziageo-web/src/components/Chart.tsx` | the graphs, plain SVG |

Reading the host asks for no privilege of the kernel: the panel runs the overview as it runs, and only the
interfaces of the tunnels need `CAP_NET_ADMIN`.
