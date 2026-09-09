# The websocket proxies

A proxy is `wstunnel` running beside the panel. A client dials it over TLS, the proxy hands the datagrams
to the interface on the loopback of the same host, and a network that passes nothing but web traffic still
carries the tunnel. The binary comes from the submodule, see [submodules.md](submodules.md).

The panel holds as many proxies as the host needs, each on a port and a path of its own. They are on the
`Proxies` page.

| Setting | What it holds |
|---|---|
| Name | The name the service and the files of the proxy are named after |
| Port | The port the proxy listens on, 443 by default |
| Path | The path a client names to reach the proxy |
| Enabled | Whether the host runs the service of the proxy |
| Path to the certificate, Path to the key | The certificate the proxy answers under, empty for the one of the panel |

A fresh proxy comes with a path of 24 random characters. It is the secret of the proxy: a request under any
other path is refused before a tunnel is opened. The certificate of the panel stands in the two fields as a
placeholder, since that is what a proxy takes when it names none of its own. The proxy answers under TLS, so
turning it on without a certificate of its own and without one on the panel is refused with `no-certificate`,
and the page says as much above the list.

## What the panel writes

Saving a proxy writes two files into `/etc/amneziageo-server` and restarts `amneziageo-proxy@<name>`:

- `proxy-<name>.yaml`, the targets the proxy may reach. One rule matches the path and allows UDP to the
  ports of the interfaces that are turned on, on `127.0.0.1` alone. A host with no interfaces up gets
  `restrictions: []`, which lets nothing through.
- `proxy-<name>.env`, the arguments of the service: the address to listen on, the whitelist and the
  certificate.

The list of ports follows the interfaces: adding, changing, applying or removing one writes the whitelist of
every proxy again. `wstunnel` rereads it on its own, without a restart. Turning a proxy off takes its
service down and leaves the files; removing it takes the service down and clears them.

## Reaching it

A client names the proxy as `wss://<host>:<port>/<path>` and the endpoint it wants as the port of the
interface. The same shape works the other way round, for an outbound of the `ws` kind that leaves through a
proxy elsewhere, see [outbounds.md](outbounds.md).
