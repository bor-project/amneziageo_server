# AmneziaGeo server privacy

Version 1, 24 October 2026

AmneziaGeo server has no telemetry and no analytics. Nothing about your server or your clients reaches its authors. The code carries no telemetry or crash-reporting library.

## Who the server exchanges data with

- Your clients. Their traffic goes through the server, and the panel hands them their configurations and subscriptions.
- GitHub (api.github.com, github.com). Checks for new versions once every 12 hours; downloads when you start an update or allowed it. For Docker, the images come from ghcr.io.
- Sources of the geo databases. By default these are github.com (the Loyalsoldier, runetfreedom and jameszeroX sets) and raw.githubusercontent.com/bor-project/amneziageo-geo. The server checks and downloads the databases once every 24 hours. The sources can be changed in the panel.
- Upstream DNS servers of the panel resolver. Only when the resolver is on, and only the addresses you set (for example, 1.1.1.1 or 8.8.4.4).
- Let's Encrypt. Only when you agreed to get a certificate in the menu.
- During installation the menu downloads packages from the repositories of your system, Docker and the server release from GitHub.

## What is kept on the server

In the database of the panel on your server:

- interfaces, clients and their keys, templates, rules;
- accounts of the panel and the log of actions in it with the addresses of sign-ins;
- traffic counters of the clients by day and the time of their latest connection;
- the connection log, only when you turned it on: for every new connection of a client, the time, the client, the address and name of the destination and what routing did with the connection. The log is kept for as many days as you set;
- the record of accepted terms.

The server sends none of it anywhere. Backups and their keeping are up to you.

## Uninstalling

The amneziageo-server menu takes the panel off the server, and the database and the settings only when you confirm it.
