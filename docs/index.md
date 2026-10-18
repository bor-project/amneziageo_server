---
title: About
lang: en
topic: home
permalink: /
description: Panel for an AmneziaWG server on Linux - interfaces, clients with configurations and subscriptions, templates, routing by geo databases and domains through other servers, a resolver for the clients, updates and backups. As a service or in Docker.
---

# AmneziaGeo Server

**English** | [Русский](index.ru.md)

AmneziaGeo Server is a panel for an AmneziaWG server on Linux. It raises the tunnel interfaces in the kernel itself, keeps the clients and hands them their configurations as a file, a QR code, a `vpn://` link and a subscription, counts their traffic, and can send that traffic further: by geo databases and domains through other servers, through the host itself or into a block. It runs on Debian and Ubuntu with the `amneziawg` kernel module, as a service or in Docker, on amd64 and arm64.

## Choose how to install

{% include kind-cards.html %}

Both ways give the same server: the container runs in the network of the host, so its interfaces, firewall rules and ports are the same as those of the service. One command puts either of them on the host: the console menu `amneziageo-server`, which then keeps the server running: updates and rollback, database backups, certificates, the firewall, users and tokens.

## What it does

- **Interfaces.** AmneziaWG server interfaces with keys, a port, addresses and all the obfuscation of profile 3.1. The panel puts them into the kernel itself, without `awg` and `awg-quick`, watches them and raises them again if the host loses them. Interfaces raised by `awg-quick` move into the panel whole, together with their clients.
- **Clients.** A key pair, addresses in the tunnel, enabling and disabling, a note, a daily traffic limit, access to the client from the tunnel. The configuration comes as a `.conf` file, a QR code and a `vpn://` link, and its name is built from a template. The list shows who is online, the speed and the traffic of the day. When a client is disabled, the AmneziaGeo app takes its tunnel down by itself on a signal from the server.
- **Templates and subscriptions.** A template gives its clients `AllowedIPs` built from geo keys, networks and domains, DNS, MTU and keepalive, and routes services behind a CDN by name through stand-in addresses of the resolver; editing the template changes the files of all its clients. A subscription serves the configurations of several devices at one address, and client apps reload it by themselves.
- **Routing on the server.** Rules by countries and site categories from geo databases, by domains, addresses, ports and clients. Traffic goes through a channel (an AmneziaWG tunnel to another server, inside WebSocket as well), through the host, or is blocked. Channel groups: by priority, round robin or per client. A route test by name and port.
- **A resolver for the clients.** The server answers the DNS queries of the clients itself, asks upstream servers over HTTPS or TLS and puts the addresses it sees into the sets of the rules: this is how rules by domains work at the kernel level.
- **WebSocket.** The tunnel inside WebSocket on the TCP port of the interface, for networks that let only web traffic through.
- **For the AmneziaGeo app.** The server hands the client app its geo sources, routing lists (presets), the subscription address and a speed test; the AmneziaGeo client for Windows, Linux and Android reads all of this by itself. Any other AmneziaWG app works with the server as with a plain AmneziaWG server.
- **Maintenance.** Updates from the panel with a rollback on failure, a database backup in one click and a move to another server from it, the event log and the connection log, and the console menu for everything done on the host.

## What you need

- Linux with the `amneziawg` kernel module on amd64 or arm64: Debian 12 or newer, Ubuntu. The console menu puts the module on from the Amnezia PPA when the kernel lacks it.
- A public address or name at which the clients find the server. The panel over HTTPS needs a name and a Let's Encrypt certificate, which the menu also gets.
- Root on the server.

## Client

The AmneziaGeo client for Windows, Linux and Android has a site of its own: [client documentation](https://bor-project.github.io/amneziageo/). What the server offers the client is told there in the section on what the server offers.

## For developers

[Building and the layout of the repository](develop/build.md), [HTTP API](develop/api.md).

## License and credits

The server is built on the [Amnezia VPN](https://github.com/amnezia-vpn) ecosystem: the AmneziaWG kernel module. The tunnel inside WebSocket is carried by [wstunnel](https://github.com/erebe/wstunnel). Default geo databases: [Loyalsoldier/v2ray-rules-dat](https://github.com/Loyalsoldier/v2ray-rules-dat), [runetfreedom](https://github.com/runetfreedom), [zkeen-ip](https://github.com/jameszeroX/zkeen-ip) and our own [amneziageo-geo](https://github.com/bor-project/amneziageo-geo).
