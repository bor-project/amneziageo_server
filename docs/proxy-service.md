# Proxy service

The proxy service supports WebSocket, HTTP GET/POST and QUIC. Direct WireGuard UDP remains available independently.

## Installation

Build the standalone package with `deploy/publish.sh --proxy`. Run its `install.sh` as root. It installs `amneziageo-proxy.service`, keeps releases under `/opt/amneziageo-proxy`, reads `/etc/amneziageo-proxy/proxies.json`, and writes runtime state under `/var/lib/amneziageo-proxy`. No VPN interface, WireGuard module, VPN panel or update service is required. The service uses no NET_ADMIN capability.

For a combined VPN installation use `install.sh --with-proxy`; `--without-proxy` disables proxy operation while retaining settings. A fresh VPN installation without an explicit selection omits the proxy service. Upgrades preserve the existing selection; legacy VPN installations keep proxy support. A whole package also accepts `install.sh --proxy-only`.

The VPN panel delegates native processes to the service over a root-only Unix socket, `/run/amneziageo-proxy/control.sock`. The panel retains HTTP listeners shared with hello, subscriptions and the panel itself. WS and HTTP backends bind loopback. QUIC binds UDP independently, including the number used by a TCP panel listener. A WireGuard endpoint already occupying that UDP port prevents a local QUIC binding. QUIC paths sharing one UDP port use one certificate.

The standalone service accepts TCP connections directly from its catalog and requires no API/database. `amneziageo-proxy init <file>` creates an empty catalog. `amneziageo-proxy credential` produces a new credential for a local client. Store credentials in the private catalog and the client; do not include them in URLs or logs. Use a separate credential per client.

`proxy-install.sh --rollback` selects the previous proxy release. `--disable` stops the service without deleting configuration. `--remove` unregisters the service while keeping configurations and releases. Read logs with `journalctl -u amneziageo-proxy.service` and health from `/var/lib/amneziageo-proxy/status.json`. Invalid catalog updates stop their listeners and report `invalid-proxy-catalog`.

## Catalog and remote hosts

Create proxies in Routing → Proxies. Select WebSocket, HTTP or QUIC. HTTP selects GET or POST. The certificate switch selects TLS; QUIC requires TLS 1.3. For HTTPS/WSS the existing certificate files and renewal mechanism apply. HTTP GET uses the AmneziaGeo mark.

The public Host/port/path are advertised to clients. The server port can override the public port; paths remain unique by the existing shared-port rules, independently of Host. `originHttp` permits a plain HTTP connection from a TLS-terminating intermediary and is distinct from the public certificate setting. Legacy entries without the certificate field keep public TLS enabled.

Clear “Run on this server” to publish a proxy hosted elsewhere. Certificate file paths then describe that host and are not read locally. `GET /api/proxies/{id}/export` under the proxy-management scope produces a standalone catalog with the credentials of clients currently selected by their templates. Transfer it through an authenticated channel to the proxy host. Repeat the export after changing template/client access. The proxy root secret, WireGuard keys and catalog-signing key are not exported. Combine entries into one version-1 service catalog when publishing several proxies.

The catalog contains `version: 1` and a `proxies` array. Each entry carries `id`, `name`, `transport` (`websocket`, `http`, `quic`), `host`, `port`, `path`, `credentials`, `useCertificate`, and optional `listenPort`, certificate paths, `originHttp` and `targets`. HTTP requires `protocolVersion: 2` and `httpMethod: GET|POST`. `targets` uses the existing `restricted`/`destinations` policy; restricted entries preserve destination boundaries for all transports. Other protocol versions are rejected.

## HTTP transport

GET is bodyless. Uplink datagrams use `X-WG-Data`; downlink uses responses to polling requests. POST carries the same framed datagrams in its body. All operations of a POST endpoint use POST, including challenge and pull. Methods are selected explicitly; there is no fallback between GET, POST, WS, QUIC or direct UDP. Datagrams are limited to2048 bytes and upload batches to4096 bytes.

Both modes use the signed v2 protocol. The credential is not sent over HTTP. HKDF request/response/challenge keys, public key identifier, boot epoch, monotonic timestamp and nonce authenticate each exchange. The request MAC includes the actual method, path, query and payload hash. Response signatures and replay rejection remain enabled. The existing `http-get.v2` key-domain labels are retained for compatibility.

For the Yandex CDN profile select HTTP without a certificate and GET. It uses neither POST nor WebSocket, CONNECT or a GET body. HTTPS configuration remains available; dedicated HTTP-over-TLS/CDN handshake debugging is tracked separately in AG-737 and is outside AG-730 acceptance.

## Compatibility and hello

The original `proxies` feature remains version1 and contains only supported GET/WebSocket projections. GET keeps the legacy `xhttp`/`vless` DTO names and includes explicit `xhttp.version: 2` and `scheme`. This is the custom signed HTTP protocol. It does not launch Xray. Plain WS, POST and QUIC are not inserted into the old catalog.

The additional `proxiesV2` feature is version2. Its entries use canonical transport names, `tls.enabled`, HTTP `{version: 2, method, dataPlacement}` and QUIC `{version: 1, protocol: webtransport, datagrams: framed-stream}`. Source identity, signatures, template order and destination restrictions persist. Older clients ignore the new feature and retain UDP/WS support.

Legacy GET prototype entries are adopted only when a persisted v2 runtime profile matches their path and known derived credentials. Their identifier, root secret, public TLS defaults, template links and restrictions remain intact. An unproven `xhttp` entry is not run or advertised as GET; the panel reports `legacy-xhttp-requires-explicit-http-conversion`. Explicitly selecting HTTP GET/POST converts it while retaining identity and credential derivation. New clients must not infer a custom GET protocol merely from an old XHTTP label without a version marker.

The QUIC runtime is built separately from the source pinned in `deploy/quic.commit`. `quic-udp-only.patch` makes the dedicated server use UDP without taking the same TCP socket. WS and signed HTTP retain their existing runtime. No Git merge is part of this build.
