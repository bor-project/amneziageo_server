# Third-party components of AmneziaGeo server

AmneziaGeo server is distributed under GPL-3.0 or later. It is built from the components below; each keeps the license of its authors.

## Tunnel

- AmneziaWG kernel module (Amnezia VPN, WireGuard LLC): GPL-2.0.
- wstunnel (Erèbe): BSD-3-Clause.

## Service

- .NET, ASP.NET Core and Entity Framework Core 10.0 (Microsoft, .NET Foundation): MIT.
- Microsoft.Data.Sqlite and Microsoft.Extensions 10.0: MIT.
- SQLitePCLRaw 3.0: Apache-2.0. SQLite: public domain.

## Panel

- React 19 and React DOM: MIT.
- React Router 7: MIT.
- Redux Toolkit 2 and React Redux 9: MIT.
- TanStack Query 5: MIT.
- axios 1: MIT.
- date-fns 4: MIT.
- qrcode 1.5: MIT.
- uuid 14: MIT.

## Geo databases

The databases are not part of the program: the server downloads them from their sources, and they come under the terms of their authors. The default sources are Loyalsoldier/v2ray-rules-dat, runetfreedom/russia-blocked-geosite and runetfreedom/russia-blocked-geoip, jameszeroX/zkeen-ip and bor-project/amneziageo-geo on GitHub.
