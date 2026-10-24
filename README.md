# AmneziaGeo Server

**English** | [Русский](README.ru.md)

AmneziaGeo Server is a panel for an AmneziaWG server on Linux. It raises the tunnel interfaces in the kernel itself, keeps the clients and hands them their configurations as a file, a QR code, a link and a subscription, counts their traffic, and can send that traffic further: by geo databases and domains through other servers, through the host itself or into a block. It runs on Debian and Ubuntu with the `amneziawg` kernel module, as a service or in Docker, on amd64 and arm64.

**[Download](https://github.com/bor-project/amneziageo_server/releases) · [Documentation](https://bor-project.github.io/amneziageo_server/) · [Client](https://github.com/bor-project/amneziageo)**

## Features

- AmneziaWG interfaces with all the obfuscation of profile 3.1, put into the kernel by the panel itself, without `awg-quick`; interfaces raised by `awg-quick` move into the panel together with their clients.
- Clients with a configuration file, a QR code, a link and a subscription; templates build `AllowedIPs` from geo keys, networks and domains.
- Routing on the server by countries, site categories, domains, addresses, ports and clients: through other servers (AmneziaWG, inside WebSocket as well), through the host or into a block.
- A resolver for the clients over HTTPS or TLS, so that rules by domains work at the kernel level.
- The tunnel inside WebSocket on the TCP port of the interface, for networks that let only web traffic through.
- Updates from the panel with a rollback on failure, database backups and the console menu `amneziageo-server` on the host.

Installation [as a service](https://bor-project.github.io/amneziageo_server/service/) or [in Docker](https://bor-project.github.io/amneziageo_server/docker/), setup and maintenance are on the **[documentation site](https://bor-project.github.io/amneziageo_server/)**.

## Client

The **[AmneziaGeo client](https://github.com/bor-project/amneziageo)** for Windows, Linux and Android reads the geo sources, routing lists, subscription address and speed test of the server by itself: see its **[documentation site](https://bor-project.github.io/amneziageo/)**. Any other AmneziaWG app works with the server as with a plain AmneziaWG server.

## For developers

Building, the layout of the repository, submodules and releases: [for developers](https://bor-project.github.io/amneziageo_server/build.html), [HTTP API](https://bor-project.github.io/amneziageo_server/api.html).

The reference by section, written together with the code:

- [serving](docs/serving.md) - what the panel listens on;
- [auth](docs/auth.md) - accounts and tokens;
- [kernel](docs/kernel.md) - how the kernel is spoken to;
- [appearance](docs/appearance.md) - the theme and the language of the interface;
- [overview](docs/overview.md) - what the overview of the host reads;
- [configs](docs/configs.md) - what a server endpoint carries;
- [clients](docs/clients.md) - what the clients of an endpoint carry;
- [templates](docs/templates.md) - what a client template sets;
- [geo](docs/geo.md) - what the geo databases carry;
- [outbounds](docs/outbounds.md) - how traffic leaves the host;
- [balancers](docs/balancers.md) - how it is spread over the ways out;
- [rules](docs/rules.md) - where it is sent;
- [dns](docs/dns.md) - how the names of the clients are answered;
- [services](docs/services.md) - what an endpoint offers its clients on its TCP port, the tunnel inside a websocket among it;
- [firewall](docs/firewall.md) - how the ports of the panel are opened in the firewall of the host;
- [install](docs/install.md) and [docker](docs/docker.md) - how the panel goes on a server;
- [updates](docs/updates.md) - how it updates itself.

## License

[GPL-3.0 or later](LICENSE). The [terms of use](TERMS.md), the [privacy notes](PRIVACY.md) and the [third-party components](THIRD-PARTY.md) lie in the root of the repository; the panel shows them at the first sign-in and on the page About.

## Credits

The server is built on the [Amnezia VPN](https://github.com/amnezia-vpn) ecosystem: the AmneziaWG kernel module. The tunnel inside WebSocket is carried by [wstunnel](https://github.com/erebe/wstunnel). Default geo databases: [Loyalsoldier/v2ray-rules-dat](https://github.com/Loyalsoldier/v2ray-rules-dat), [runetfreedom](https://github.com/runetfreedom), [zkeen-ip](https://github.com/jameszeroX/zkeen-ip) and our own [amneziageo-geo](https://github.com/bor-project/amneziageo-geo).
