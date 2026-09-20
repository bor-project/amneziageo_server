# The proxies

A proxy is a way into the interfaces of the host that is not their own UDP port. The panel holds as many as
the host needs, each on a port of its own, and runs one service per proxy. They are on the `Proxies` page.

A proxy comes in one of two kinds, picked when it is added:

| Kind | What answers | What it takes in |
|---|---|---|
| `ws` | `wstunnel` from the submodule, see [submodules.md](submodules.md) | the tunnel inside a websocket under TLS, on a secret path |
| `wg` | the relay of the panel, `AmneziaGeo.Server.Cli relay` | the datagrams of the tunnel as they are, on another port |

| Setting | Kind | What it holds |
|---|---|---|
| Name | both | The name the service and the files of the proxy are named after |
| Kind | both | Which of the two the proxy is |
| Port | both | The port the proxy listens on: for a fresh `ws` proxy the port of the first interface that is turned on, for a fresh `wg` proxy and on a host with no interface on 443 |
| Enabled | both | Whether the host runs the service of the proxy |
| Open the port in the firewall | both | Whether the panel holds the port of the proxy open in the firewall of the host, see [firewall.md](firewall.md) |
| Allowed from | both | The addresses and the networks the proxy takes, empty for any |
| Path | `ws` | The path a client names to reach the proxy |
| Path to the certificate, Path to the key | `ws` | The certificate the proxy answers under, empty for the one of the panel |
| Forward to | `wg` | The host and the port the datagrams go on to |

Two proxies of the same kind cannot share a port; a `ws` proxy and a `wg` proxy can, since one listens on TCP
and the other on UDP. A `ws` proxy can take the port of an endpoint for the same reason, and a fresh one takes the
port of the first endpoint that is on.

## The websocket kind

A fresh `ws` proxy comes with no target and a path of 24 random characters. The path is the secret of the proxy: a request under
any other path is refused before a tunnel is opened. The certificate of the panel stands in the two fields as
a placeholder, since that is what a proxy takes when it names none of its own. The proxy answers under TLS,
so turning it on without a certificate of its own and without one on the panel is refused with
`no-certificate`. Such a proxy stands stopped in red in the list, with the reason under the pointer, and its form
says as much under the certificate.

Saving it writes two files into `/etc/amneziageo-server` and restarts `amneziageo-proxy@<name>`:

- `proxy-<name>.yaml`, the targets the proxy may reach. One rule matches the path and allows UDP to the
  ports of the interfaces that are turned on, on `127.0.0.1` alone. A host with no interfaces up gets
  `restrictions: []`, which lets nothing through.
- `proxy-<name>.env`, the arguments of the service: the address to listen on, the whitelist and the
  certificate.

The list of ports follows the interfaces: adding, changing, applying or removing one writes the whitelist of
every proxy again. `wstunnel` rereads it on its own, without a restart.

A service is started over only when one of its files changes or it is not running, so the panel starting
over leaves the tunnels through a proxy alone. `wstunnel` rereads a renewed certificate on its own too.

A host that runs no systemd, a container among them, gets no services: the panel runs `wstunnel` and the relay
itself with the same arguments and starts one again three seconds after it falls over. Such a proxy goes down
with the panel, see [docker.md](docker.md).

A client names the proxy as `wss://<host>:<port>/<path>` and the endpoint it wants as the port of the
interface. A client of AmneziaGeo reads the front from the line `# AmneziaGeo WebSocket` of its file, see
[clients.md](clients.md), and dials the host and the port of the endpoint when the file names none. The same shape works the other way round, for an outbound of the `ws` kind that leaves through a
proxy elsewhere, see [outbounds.md](outbounds.md).

## The wireguard kind

A `wg` proxy takes AmneziaWG on one port and passes it to the target, a socket per client, and carries what
the target answers back. Nothing is unwrapped and nothing is added, so the client needs no second piece of
software: it names the proxy as its endpoint and nothing else changes. A fresh one takes the first interface
that is turned on as its target; any host and port can stand there instead, which is what puts the way in on
one machine and the interface on another.

Saving it writes `proxy-<name>.env` into `/etc/amneziageo-server` and restarts `amneziageo-relay@<name>`. The
service runs `AmneziaGeo.Server.Cli relay --listen 0.0.0.0:<port> --target <target>`, and a client with
nothing coming through for three minutes is forgotten. There is no whitelist file: the target is the one the
panel wrote, and a client names no target of its own. As with `ws`, the service is started over only when its
file changes or it is not running.

## The sources a proxy takes

A proxy that names no source answers whoever reaches its port. Naming addresses and networks, sixteen at
most, holds it to them: the panel writes the table `inet amneziageo_proxy` with one input chain and drops
what comes to the port of the proxy from anywhere else. A `ws` proxy is held on TCP, a `wg` proxy on UDP,
and a family the proxy names nothing in is dropped whole, so a list of IPv4 networks closes the port to
IPv6. An address stands for itself, a network is written as `10.1.0.0/16` and takes the whole range. The
field offers the addresses the host carries and leaves out the ones already named.

The table carries the proxies that are turned on and name sources, and is written again whenever a proxy is
added, changed, turned on or off, or removed. Laying it takes `CAP_NET_ADMIN`: a proxy that names sources is
not started while the firewall refuses the ruleset, and the page carries what it said.

Turning a proxy off takes its service down and leaves the files; removing it takes the service down and
clears them.
