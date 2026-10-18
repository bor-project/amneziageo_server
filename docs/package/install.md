---
title: Installation
lang: en
kind: package
topic: install
description: The package in one command of the menu or by hand, updates and rollback, what lies on the host, uninstalling.
---

# Installation

You need Debian 12 or newer or Ubuntu on amd64 or arm64, root, a public address and about 2 GB of free disk space. The panel raises the tunnel interfaces through the `amneziawg` kernel module, so the module goes on first; the console menu does this itself. Docker is not needed on this host. If it is already there, it turns the policy of the `FORWARD` chain to `DROP`, and the clients go no further than the server: the installation adds `"ip-forward-no-drop": true` to `/etc/docker/daemon.json` where the version of Docker takes it, and lifts a `DROP` already set where ufw does not hold the chain.

## In one command

Every release carries the `amneziageo-server` script, the [console menu](menu.md), which puts the panel on a clean host and keeps it running afterwards. As root:

```
curl -fsSL https://github.com/bor-project/amneziageo_server/releases/latest/download/amneziageo-server \
  -o /usr/local/bin/amneziageo-server
chmod 755 /usr/local/bin/amneziageo-server
amneziageo-server install
```

`install` first looks the host over and prints what it carries: the system and the kernel, the upgrades waiting, the `amneziawg` module, memory and disk, Docker and its containers, forwarding and BBR, ufw, the ports taken with the services behind them, the AmneziaWG and WireGuard interfaces, the Let's Encrypt certificates. It stops on what the panel cannot live with: no release for the architecture, a container host without the module. Then it asks all its questions at once, with defaults taken from the look, prints the plan and, once you agree, does everything on the list.

### Questions

| Question | Default | Key of the answers file |
|---|---|---|
| `Install as`: 1 Package, 2 Docker | Package when the host has no Docker | `kind`: `package` or `docker` |
| `Put a short command for amneziageo-server on the host?`, `Name of the short command` | asked only on a terminal; `ag-s` or the first free name with a number | `shortcut`: the name, empty for none |
| `Upgrade the system first (apt full-upgrade)?` | yes | `upgrade` |
| `Channel`: 1 Stable, 2 Test | Stable | `channel`: `stable` or `test` |
| `Backup file to restore, empty for a fresh panel` | empty | `restore` |
| `Turn BBR on?` | yes | `bbr` |
| `Login of the first administrator` | `admin` | `login` |
| `Make <login> an administrator of the panel?`, when the login is a user of the host | yes | `host_admin` |
| `Make up a temporary password for it, replaced at the first sign-in?` | yes | `generate` |
| `Name of the server for a certificate of Let's Encrypt, empty for none` | the name the host already holds a certificate for | `domain` |
| `Email for Let's Encrypt`; `Agree to the terms of Let's Encrypt` | empty; yes | `email`, `terms` |
| `Port of the panel` | 8443 or the first free one above it | `port` |
| Without a name: `Let the panel answer on every address of the host, without TLS?` | no | `everywhere` |
| `Make the first interface?` | yes | `endpoint` |
| `Name of the interface`; `UDP port of the interface, its services on TCP of the same number`; `Address clients reach the server at` | `awg0`; 51820 or the first free one; the name of the server or the public address of the host | `endpoint_name`, `endpoint_port`, `endpoint_host` |
| `Carry the tunnel in a websocket too, on TCP <port>?` | no | `websocket` |
| `Name of the first client, empty for none`; `Show its configuration as a QR code?` | `client1`; yes | `client`, `qr` |
| `Answer the DNS of its clients on this server, asking outside over HTTPS?` | yes | `resolver` |
| (not asked) the IPv6 range of the clients of the first interface, its first address the address of the interface | on a host with a global IPv6 address a random range of its own, `fdXX:XXXX:XXXX::1/64`, otherwise no IPv6 | `ipv6_range`: like `fd3a:4b5c:6d7e::1/64`, empty for none |
| `Open <ports> in ufw?`, or `Turn ufw on, letting in ssh and what listens now?` | yes when ufw is not installed | `ufw` |
| `ufw keeps the clients of <interfaces> from the resolver of the panel: let them reach it?`, at the end, when ufw keeps the clients of interfaces from the resolver | yes | `resolver_ufw` |
| `Reboot now?` after an upgrade with a new kernel | | `reboot` |
| `Go on?` past the plan | | `go` |

The answers can be put into a file beforehand as `key=value` lines and given with `amneziageo-server install --answers <file>`: the script then asks only for what the file lacks, and with every key it runs without a question.

### What it does

- upgrades the system; if a new kernel came, it reboots the host, and the same command goes on with the same answers after the reboot;
- puts on the module from the Amnezia PPA (`amneziawg-dkms`) when the kernel lacks it, turns packet forwarding on now and at boot, and turns BBR on; the interface on which the host takes its IPv6 route from router advertisements gets `accept_ra = 2`, otherwise with forwarding on the kernel would drop that route at once;
- if Docker is on the host, lets the packets of the clients past its `DROP`, as said above;
- downloads the package of the newest release of the channel, checks it against the key the releases are signed with and installs it with its `install.sh`;
- makes the first administrator; the temporary password is printed once at the end;
- gets a Let's Encrypt certificate for the name of the server (`certbot` with an RSA key, an HTTP-01 check on TCP 80, renewal by the `certbot.timer` timer, see [Certificates](panel/certificates.md#lets-encrypt)) or takes the one the host already holds, and opens the panel on every address of the host;
- makes the first interface and client through the API of the panel: the configuration of the client goes to `/root` and, when asked, is printed as a QR code; on a host with a global IPv6 address the interface gets a range of unique local IPv6 addresses, and the clients go over IPv6 through the server;
- turns the resolver for the clients on, if you agreed;
- opens the ports in ufw or turns ufw on, keeping ssh and what already listened open;
- if ufw keeps the clients of some interface from the resolver of the panel, offers to let them in with the rule `allow in on <interface> to any port 53`, see [DNS](panel/dns.md#how-it-works);
- puts the short command of the menu on, if you agreed: a link `/usr/local/bin/ag-s` to `amneziageo-server`, or with the name you gave; a name another command already holds is not taken, and the script asks for another one;
- checks the service, the panel, the certificate and the interface and prints the address of the panel, the login and the password, the ports of the panel and the interface, the IPv6 address of the host and the command of the menu with its short command.

On a host installed without it, `ln -s /usr/local/bin/amneziageo-server /usr/local/bin/ag-s` puts the short command on; an update leaves the link alone, uninstalling the panel takes it off.

Next: [First run](first-run.md): signing in, changing the password, the first interface and client if the script did not make them.

### Moving from another server

On the old panel "Overview" > "Backup" > "Download a copy"; the file is copied to the new host:

```
amneziageo-server install --restore /root/amneziageo-<name>-<time>.db
```

The script checks the file, installs the panel and starts it on the database from the backup: accounts and tokens, interfaces with their keys, clients, rules and settings move as they are; it makes no administrator and no interface. The clients reach the server by the name in their configurations, so the name in DNS is moved to the new host. The certificate, the signing key and the geo files are not in the backup: the script offers to get a certificate for the same name once the name already points at the new host, drops the addresses of the old host from the settings of the panel, and the panel downloads the geo sources after it starts. On a running panel `amneziageo-server restore <file>` does the same. More in [Backup](panel/overview.md#backup).

## By hand

The same path is described in the reference of the repository, [install.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/install.md), and step by step in Russian in [Панель AmneziaGeo на Debian 12 без Docker](https://github.com/bor-project/amneziageo_server/blob/master/docs/ru/install-debian12.md). In short:

1. Upgrade the system and reboot; install the kernel headers, `nftables`, `dkms`, `gcc`, `make`.
2. Install `amneziawg-dkms` from the Amnezia PPA and load the module: `modprobe amneziawg`, the line `amneziawg` in `/etc/modules-load.d/amneziawg.conf`. The `amneziawg-tools` package is not needed: the panel talks to the module itself.
3. Turn forwarding on: `net.ipv4.ip_forward = 1` and `net.ipv6.conf.all.forwarding = 1` in `/etc/sysctl.d/90-amneziageo.conf`, then `sysctl --system`. If the host takes its IPv6 from router advertisements (SLAAC), the first line of the same file is `net/ipv6/conf/<external interface>/accept_ra = 2`: with forwarding on, the kernel drops such a route at once and takes no new advertisements.
4. Download from the [Releases](https://github.com/bor-project/amneziageo_server/releases) page the package `amneziageo-server-<version>-linux-x64.tar.gz` (`-arm64` for arm64) and `update.json` of the same release, check the `sha256` of the package against the manifest, unpack it and run the installer:

```
tar xzf /root/amneziageo-server-<version>-linux-x64.tar.gz -C /root
/root/amneziageo-server/install.sh
```

`install.sh` puts the release into `/opt/amneziageo-server/releases/<release>` and the web interface into `/opt/amneziageo-server/web/<release>`, and points the `current` and `wwwroot` links at them; it makes `/var/lib/amneziageo-server` for the database and `/etc/amneziageo-server` with a `server.env` stub where every line is commented out; it installs the services `amneziageo-server.service` and `amneziageo-proxy@.service`; it puts the menu into `/usr/local/bin/amneziageo-server`; it enables the service at boot but does not start it: an administrator is needed first, see [First run](first-run.md). At the end it writes `<release> is on the host, the server is not running` and the two next commands.

The service runs as root: reading and writing an interface need `CAP_NET_ADMIN`, and the interface files belong to root. A separate account with `AmbientCapabilities=CAP_NET_ADMIN` and the files handed over to it does the same.

## Updates

The panel installs a new release by itself. Every 12 hours it asks GitHub about releases, checks the signature of the `update.json` manifest with the release key and, if the release is newer, shows it in the header of "Overview" next to its own version. The "Update" button, for a role with the "Update the panel" permission, assembles the package for the architecture of the host: the files the host already has are copied, the rest are downloaded from the release in parts (usually a megabyte or two out of about 55), each is checked against its digest, and the `install.sh` of the package is run apart from the panel, through `systemd-run`. The page follows the steps "Download", "Install", "Restart", "Done" and loads the new panel by itself once it answers; a failed update stays in the header with its log until it is hidden with the "Hide" cross or the next one goes through. More in [Updates](panel/overview.md#updates).

The `install.sh` of the new release puts it next to the working one, stops the service, copies the database to `/var/lib/amneziageo-server/backup`, moves `current` to the new release and starts the service. It waits until the server says it is up and watches it for five more seconds: a release that did not come up or fell over in that time rolls back by itself: the copy of the database comes back, `current` points at the previous release, the service starts on it, and the script ends with an error. A service stopped before the update stays stopped.

Interfaces and their clients live in the kernel and do not break during an update:

| What | During the restart |
|---|---|
| interfaces and clients | stay in the kernel with their handshakes and counters |
| channels | keep the server as a peer, with the handshake and counters; a channel inside WebSocket is carried by the server itself and waits for it |
| routing rules and firewall tables | stay on the host and are put again in one step at the start |
| addresses the resolver put into the sets | are read from the host at the start and put again with the rules |
| resolver | does not answer for the seconds the server is starting; the clients ask again |
| WebSocket fronts | keep working: a front restarts only if its files changed |
| panel and subscriptions | do not answer while the server is starting |

From the console `amneziageo-server update` and `amneziageo-server update beta` do the same (menu items 2 and 3): the script asks the panel to update and waits until it answers with the new version. The panel takes pre-releases after "Receive prerelease versions" is checked on the [Server](panel/server.md) tab of the settings. By hand: unpack the new package and run its `install.sh`, as the first time; a package the host already carries is not installed again without `--force`.

The web interface alone, while the server runs, is installed by `install.sh --ui` of the package or by the `amneziageo-web.tar.gz` package: the script puts the interface next to the one the server serves and moves `wwwroot` to it, and the next page opened is already new; a page that is open keeps working, because the files it may still ask for are copied from the previous interface. The package knows which server it was built for and refuses a host with another one; `--force` installs it anyway.

## Rollback

```
amneziageo-server rollback
```

Menu item 4 or `/opt/amneziageo-server/current/install.sh --rollback` moves `current` to the previous release and starts the service on it, with the web interface of that release; the same command moves back to the new one. The database stays as it is: before every update and restore it is copied to `/var/lib/amneziageo-server/backup`, where the last five copies lie, and after a rollback the menu offers to bring one of them back. `install.sh --list` prints the releases on the host.

## What lies on the host

| Path | What |
|---|---|
| `/opt/amneziageo-server/releases/<release>` | a release of the server and the utility |
| `/opt/amneziageo-server/web/<release>` | a release of the web interface |
| `/opt/amneziageo-server/current` | the working release; `AmneziaGeo.Server.Api` and `AmneziaGeo.Server.Cli` in `/opt/amneziageo-server` lead into it |
| `/opt/amneziageo-server/previous` | the previous release, where a rollback goes |
| `/opt/amneziageo-server/wwwroot` | the web interface the server serves |
| `/opt/amneziageo-server/current/wstunnel` | the WebSocket proxy of the release; the package leaves a `/usr/local/bin/wstunnel` of the host alone |
| `/var/lib/amneziageo-server/server.db` | the database: accounts, interfaces, clients, rules, settings |
| `/var/lib/amneziageo-server/geo` | geo database files |
| `/var/lib/amneziageo-server/backup` | the last five copies of the database, from before updates and restores |
| `/var/lib/amneziageo-server/update` | downloads, logs and the record of the last update |
| `/var/lib/amneziageo-server/journal.db`, `access.db` | the event log and the connection log, not part of the backup |
| `/etc/amneziageo-server/server.env` | start variables: the address before the first start, the certificate, `Update__*` and others |
| `/etc/amneziageo-server/signing.pem` | the key the sign-in tokens are signed with |
| `/etc/amneziageo-server/proxy-<interface>.env`, `.yaml` | the arguments and the target list of the WebSocket front of an interface |
| `/etc/amnezia/amneziawg/<interface>.conf` | the interface files the panel reads and writes |
| `/etc/systemd/system/amneziageo-server.service`, `amneziageo-proxy@.service` | the service of the panel and the service of a front |
| `/usr/local/bin/amneziageo-server` | the console menu, comes with every release |
| `/usr/local/bin/ag-s` | the short command of the menu, a link to `amneziageo-server`, if it was put on |

The host keeps the working release and the previous one, each with its web interface, and the interface served before the last one; the rest is removed, including a release that did not come up. A panel installed before releases existed moves into the release `legacy-<date>` at its first update.

## Uninstalling

Menu item 6 `Uninstall` asks whether to take the panel off and whether to take its database and settings with it. It stops and removes the service of the panel and of the fronts, removes `/opt/amneziageo-server`, takes the interfaces of the panel, its nftables tables and the ufw rules with its mark off the host, and removes the menu and its short commands from `/usr/local/bin`. The database in `/var/lib/amneziageo-server` and the settings in `/etc/amneziageo-server` stay unless you agree to remove them; the interface files go with them. The `amneziawg` module and the forwarding file `/etc/sysctl.d/90-amneziageo.conf` stay on the host.
