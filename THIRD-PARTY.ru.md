# Сторонние компоненты сервера AmneziaGeo

Сервер AmneziaGeo распространяется по лицензии GPL-3.0 или более поздней. Он собран из компонентов ниже, у каждого своя лицензия его авторов.

## Туннель

- Модуль ядра AmneziaWG (Amnezia VPN, WireGuard LLC): GPL-2.0.
- wstunnel (Erèbe): BSD-3-Clause.

## Служба

- .NET, ASP.NET Core и Entity Framework Core 10.0 (Microsoft, .NET Foundation): MIT.
- Microsoft.Data.Sqlite и Microsoft.Extensions 10.0: MIT.
- SQLitePCLRaw 3.0: Apache-2.0. SQLite: общественное достояние.

## Панель

- React 19 и React DOM: MIT.
- React Router 7: MIT.
- Redux Toolkit 2 и React Redux 9: MIT.
- TanStack Query 5: MIT.
- axios 1: MIT.
- date-fns 4: MIT.
- qrcode 1.5: MIT.
- uuid 14: MIT.

## Базы гео

Базы не входят в программу: сервер скачивает их из источников, и они идут на условиях своих авторов. Источники по умолчанию: Loyalsoldier/v2ray-rules-dat, runetfreedom/russia-blocked-geosite и runetfreedom/russia-blocked-geoip, jameszeroX/zkeen-ip и bor-project/amneziageo-geo на GitHub.
