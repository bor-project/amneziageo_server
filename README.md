# AmneziaGeo Server

Management server for kernel AmneziaWG: interfaces, clients, keys and traffic accounting.

## Layout

| Path | Holds |
|---|---|
| `amneziageo-server/AmneziaGeo.Server.Core` | domain types, interfaces, rules |
| `amneziageo-server/AmneziaGeo.Server.Dal` | SQLite store and schema migrations |
| `amneziageo-server/AmneziaGeo.Server.Awg` | kernel control through the awg tools |
| `amneziageo-server/AmneziaGeo.Server.Auth` | accounts, roles, passwords and tokens |
| `amneziageo-server/AmneziaGeo.Server.Api` | HTTP API and the built web interface |
| `amneziageo-server/AmneziaGeo.Server.Cli` | console administration |
| `amneziageo-web` | React interface |
| `amneziageo-tests` | tests |
| `amneziawg` | kernel module and tools as submodules |
| `deploy` | systemd unit and package files |

## Build

```
dotnet build AmneziaGeo.Server.slnx
npm --prefix amneziageo-web install
npm --prefix amneziageo-web run build
```
