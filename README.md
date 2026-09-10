# AmneziaGeo Server

Management server for kernel AmneziaWG: interfaces, clients, keys and traffic accounting.

## Layout

| Path | Holds |
|---|---|
| `amneziageo-server/AmneziaGeo.Server.Core` | domain types, interfaces, rules |
| `amneziageo-server/AmneziaGeo.Server.Dal` | EF Core over SQLite, identity stores and migrations |
| `amneziageo-server/AmneziaGeo.Server.Awg` | kernel control over generic netlink |
| `amneziageo-server/AmneziaGeo.Server.Auth` | accounts, roles as claims, passwords and tokens |
| `amneziageo-server/AmneziaGeo.Server.Api` | HTTP API and the built web interface |
| `amneziageo-server/AmneziaGeo.Server.Cli` | console administration |
| `amneziageo-web` | React interface |
| `amneziageo-tests` | tests |
| `amneziawg` | kernel module as a submodule |
| `wstunnel` | websocket proxy as a submodule |
| `deploy` | systemd unit and package files |

## Build

```
dotnet build AmneziaGeo.Server.slnx
npm --prefix amneziageo-web install
npm --prefix amneziageo-web run build
```

## Run

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Api
```

The panel is at `http://<address>:8443/`. While the interface is being worked on, `npm --prefix
amneziageo-web run dev` serves it at port 5173 with reloading and sends `/api` on to the server.

The first administrator is made once, from the console:

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Cli -- init
```

Interfaces are read and changed from the console as well:

```
sudo dotnet run --project amneziageo-server/AmneziaGeo.Server.Cli -- device show
```

What the panel listens on is in [docs/serving.md](docs/serving.md), how accounts and tokens work is in
[docs/auth.md](docs/auth.md), how the kernel is spoken to is in [docs/kernel.md](docs/kernel.md), the theme
and the language of the interface are in [docs/appearance.md](docs/appearance.md), what the overview
of the host reads is in [docs/overview.md](docs/overview.md), what a server endpoint carries is in
[docs/configs.md](docs/configs.md), what the clients of an endpoint carry is in
[docs/clients.md](docs/clients.md), what a client template sets is in [docs/templates.md](docs/templates.md),
what the geo databases carry is in [docs/geo.md](docs/geo.md),
how traffic leaves the host is in [docs/outbounds.md](docs/outbounds.md), how it is spread over the
ways out is in [docs/balancers.md](docs/balancers.md), where it is sent is in
[docs/rules.md](docs/rules.md), how the names of the clients are answered is in
[docs/dns.md](docs/dns.md), how the tunnels are carried inside a websocket is in
[docs/proxy.md](docs/proxy.md), and how the panel goes on a server is in
[docs/install.md](docs/install.md).
