---
title: Installation
lang: en
kind: docker
topic: install
description: The container in one command of the menu or with your own compose.yaml, updates and rollback, what lies on the host, how it differs from the package, uninstalling.
---

# Installation

The container brings only the panel, the utility, the WebSocket proxy and the menu. The host gives it the rest:

- the `amneziawg` module in the kernel: `modprobe amneziawg`, the line `amneziawg` in `/etc/modules-load.d/`, so that it comes up with the host;
- Docker with its compose plugin and a running daemon;
- packet forwarding in both families: the container does not write to `/proc/sys` of the host, so without it no interface comes up, and applying an interface answers `forwarding is off in /proc/sys/...`:

  ```
  printf 'net.ipv4.ip_forward = 1\nnet.ipv6.conf.all.forwarding = 1\n' > /etc/sysctl.d/90-amneziageo.conf
  sysctl --system
  ```

  If the host takes its IPv6 from router advertisements (SLAAC), the first line of the same file is `net/ipv6/conf/<external interface>/accept_ra = 2`: with forwarding on, the kernel drops such a route at once and takes no new advertisements;

- a `FORWARD` chain that lets packets through: as it starts, Docker turns forwarding on itself and turns the policy of the chain to `drop`, and the clients of the tunnel go no further than the host. The daemon keeps its hands off the policy with `"ip-forward-no-drop": true` in `/etc/docker/daemon.json` and a restart of `docker`. The option only keeps the `drop` from coming; one already set is lifted, once, by `iptables -P FORWARD ACCEPT`, and by `ip6tables -P FORWARD ACCEPT` for IPv6. An older Docker does not take the option; forwarding turned on at boot keeps it from setting the `drop`, since Docker sets it only as it turns forwarding on itself.

The container names what of this the host lacks in its log as it starts. You need Debian 12 or newer or Ubuntu on amd64 or arm64, root, a public address and about 2 GB of free disk space.

## In one command

Every release carries the `amneziageo-server` script, the [console menu](menu.md), which puts the panel on a clean host and keeps it running afterwards. As root:

```
curl -fsSL https://github.com/bor-project/amneziageo_server/releases/latest/download/amneziageo-server \
  -o /usr/local/bin/amneziageo-server
chmod 755 /usr/local/bin/amneziageo-server
amneziageo-server install
```

The script looks the host over, asks all its questions at once, prints the plan and, once you agree, does everything on the list. Answer the first question, `Install as`, with `2` (Docker). If the host lacks Docker, its compose plugin, or the Docker daemon does not answer, the script prints the commands that put on what is missing from the repository of Docker and exits without changing anything; once they are on, the same command goes on. The questions, the keys of the answers file for `install --answers <file>` and the steps are shared with the package, see [Installation as a package](../package/install.md#questions); Docker adds two:

| Question | Default | Key of the answers file |
|---|---|---|
| `Directory for compose.yaml` | `/opt/amneziageo-docker` | `docker_dir`: a new or empty directory |
| `Put the command amneziageo-server on the host, to run the menu and the utility without entering the container?` | yes | `host_menu`: `yes` or `no` |

The installation asks for the short command of the menu (`shortcut`) only when the command goes on the host. For Docker the script also:

- adds `"ip-forward-no-drop": true` to `/etc/docker/daemon.json` where the version of Docker takes it, and lifts the `drop` Docker already put on forwarding where ufw does not hold the chain; it does not restart Docker;
- writes `compose.yaml` into the directory you named, with the image of the newest release of the channel from `ghcr.io/bor-project/amneziageo-server`, directories of the host for the database and the settings and the Docker socket for updates, starts the container and makes the first administrator in it;
- on a host that ufw guards, gives ufw what the panel cannot give it from the container: the ports of the panel and the interface, the passage of the clients of the first interface (`route allow in` and `out on <interface>`) and, with the resolver, their DNS to the server (port 53 over UDP and TCP);
- with yes to `Put the command amneziageo-server on the host`, puts the menu of the release into `/usr/local/bin/amneziageo-server`: it drives the container from outside with the same menu items, including those that change the host itself, and passes the commands of the utility into the container (`amneziageo-server user list`); it finds the project in the directory you named or by the labels of its container. With no, no command stays on the host, and the menu opens in the container: `docker compose -f <directory>/compose.yaml exec panel amneziageo-server`;
- prints at the end the directory of `compose.yaml`, the ports of the panel and the interface, the IPv6 address of the host and the command of the menu.

Next: [First run](first-run.md).

### Moving from another server

`amneziageo-server install --restore <file>` puts the container on and, while it is stopped, puts a database backup downloaded from another panel in place of the database; `amneziageo-server restore <file>` does the same on a running panel. More in [Installation as a package](../package/install.md#moving-from-another-server) and [Backup](panel/overview.md#backup).

## By hand

The same path is described in the reference of the repository, [docker.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/docker.md), and step by step in Russian in [Панель AmneziaGeo на Debian 12 в Docker](https://github.com/bor-project/amneziageo_server/blob/master/docs/ru/install-debian12-docker.md). In short: the module from the Amnezia PPA, forwarding and Docker with `ip-forward-no-drop` as above, then the compose project. The version of the image is the version of the release, the `latest` tag leads to the newest stable release; the image is built for amd64 and arm64.

```
VER=<version>
mkdir -p /opt/amneziageo-docker /var/lib/amneziageo-server /etc/amneziageo-server /etc/amnezia/amneziawg
cd /opt/amneziageo-docker
printf 'AMNEZIAGEO_TAG=%s\n' "$VER" > .env
cat > compose.yaml <<'EOF'
name: amneziageo-server

services:
  panel:
    image: ghcr.io/bor-project/amneziageo-server:${AMNEZIAGEO_TAG:-latest}
    network_mode: host
    cap_add:
      - NET_ADMIN
    env_file:
      - path: server.env
        required: false
    volumes:
      - /var/lib/amneziageo-server:/var/lib/amneziageo-server
      - /etc/amneziageo-server:/etc/amneziageo-server
      - /etc/amnezia/amneziawg:/etc/amnezia/amneziawg
      - /var/run/docker.sock:/var/run/docker.sock
    logging:
      driver: json-file
      options:
        max-size: 10m
        max-file: "3"
    restart: unless-stopped
EOF
docker compose pull
docker compose up -d
docker compose exec panel amneziageo-server init --user admin
```

- `network_mode: host` and `NET_ADMIN`: the panel raises interfaces and writes rules in the kernel of the host, so its network is the network of the host.
- Directories of the host instead of volumes: the database and the signing key lie in plain sight and are simple to copy and bring back. The `deploy/docker/compose.yaml` of the repository, which builds the image from source, keeps them in the volumes `data` and `settings`.
- `/var/run/docker.sock`: through it the panel updates itself with a button, recreating its container on the new image. The socket gives the container the whole host: whoever owns the panel owns the host as well. Without this line updates stay manual.
- The version comes from `.env` next to `compose.yaml`: an update with the button writes the new tag there, so a later `docker compose up -d` by hand brings up the same version.
- `server.env` next to `compose.yaml` takes the same variables as `/etc/amneziageo-server/server.env` of the package. The certificate is read inside the container, so its directory goes into `volumes` too: `/etc/letsencrypt:/etc/letsencrypt:ro`; the menu adds this line to `compose.override.yaml` itself.
- `init --user` is required in the container: the accounts of the host do not reach into it.

The health check asks `/api/health` at the address the panel writes into the `health` file next to the database as it starts, so it follows the panel to another port, path and TLS; `AMNEZIAGEO_HEALTH` in the environment sets another address.

## Updates

The panel installs a new release by itself when `compose.yaml` gives it the socket of the daemon. Every 12 hours it asks GitHub about releases, checks the signature of the `update.json` manifest and shows a new release in the header of "Overview" next to its own version. The "Update" button pulls the image of the release by digest, copies the database to `/var/lib/amneziageo-server/backup`, writes `AMNEZIAGEO_TAG=<version>` into the `.env` of the project, stops the panel and recreates its container on the new image with the same settings: only the image changes. The previous container waits under its own name: if the new one does not become healthy within three minutes, the database and `.env` come back, and the old one runs again. The compose files are not read for this, so the update works wherever they lie; `.env` takes the tag so that a later `docker compose up -d` by hand brings up the same image. More in [Updates](panel/overview.md#updates).

The layers of the image go from those that change rarely to those every release changes, so the daemon pulls a megabyte or two. Interfaces and clients live in the kernel and do not break during an update; the proxy, the resolver, the panel and subscriptions are away for the seconds the container is starting.

From the console `amneziageo-server update` and `amneziageo-server update beta` do the same (menu items 2 and 3); run from inside the container, the menu session ends with it, and `docker exec -it <container> amneziageo-server` opens it again. Pre-releases: "Receive prerelease versions" on the [Server](panel/server.md) tab. By hand: a copy of the database, a new tag, a restart:

```
cd /opt/amneziageo-docker
cp -a /var/lib/amneziageo-server /root/amneziageo-db-$(date -u +%Y%m%d-%H%M%S)
printf 'AMNEZIAGEO_TAG=%s\n' "<version>" > .env
docker compose pull
docker compose up -d
```

A manual update does not copy the database aside, so the copy is taken by hand.

## Rollback

`amneziageo-server rollback` (menu item 4, from the host) brings the container up on the nearest older image of the same name left on the host and offers to bring back a copy of the database from `/var/lib/amneziageo-server/backup`. By hand it is the same move with the previous tag in `.env` and `docker compose up -d`; the images of earlier versions stay on the host (`docker images | grep amneziageo`). The database stays as the new panel left it; a copy brings it back while the container is stopped.

## What lies on the host

| Path | What |
|---|---|
| `/opt/amneziageo-docker/compose.yaml` | the description of the container, in the directory named at the installation; next to it `.env` with the tag, `server.env` and the `compose.override.yaml` of the menu |
| `/var/lib/amneziageo-server/server.db` | the database: accounts, interfaces, clients, rules, settings |
| `/var/lib/amneziageo-server/geo` | geo database files |
| `/var/lib/amneziageo-server/backup` | copies of the database from before updates and restores |
| `/var/lib/amneziageo-server/update` | downloads, logs and the record of the last update |
| `/var/lib/amneziageo-server/journal.db`, `access.db` | the event log and the connection log, not part of the backup |
| `/etc/amneziageo-server/signing.pem` | the key the sign-in tokens are signed with |
| `/etc/amneziageo-server/proxy-<interface>.env`, `.yaml` | the arguments and the target list of the WebSocket front of an interface |
| `/etc/amnezia/amneziawg/<interface>.conf` | the interface files the panel reads and writes |
| `/var/run/docker.sock` | the socket of the daemon through which the panel updates itself |
| `/usr/local/bin/amneziageo-server` | the menu on the host, if you agreed to put it there at the installation; the image holds the same menu: `docker compose exec panel amneziageo-server` |
| `/usr/local/bin/ag-s` | the short command of the menu, a link to `amneziageo-server`, if it was put on |

In the image: `/opt/amneziageo-server` with the server, the utility and the web interface, `/usr/local/bin/wstunnel`, the scripts of the container and the menu of the release.

## How it differs from the package

| What | In the container |
|---|---|
| WebSocket fronts | the panel starts `wstunnel` itself and restarts it three seconds after a crash; the fronts go down and come up with the container |
| firewall | the image has no ufw, the panel puts its own nftables tables; on a host with ufw on, its `drop` is stronger: the installation opens the ports and the passage of the clients of the first interface in ufw, and what is made later is opened in the ufw of the host by hand; the panel calls such a port closed and prints the command `ufw allow <port>` |
| host accounts | off: the panel signs in only with its own accounts and makes no system users |
| restart from the panel | the server exits, and the restart policy of compose brings the container up again |
| web interface alone | is not updated separately: every image carries the server and the interface together |
| menu items that change the host | from the container, rollback to an old image, uninstalling and a Let's Encrypt certificate answer that the container does not reach the host, while ufw rules, BBR and forwarding print the command for the host; the menu on the host does it all itself |

## Uninstalling

Menu item 6 `Uninstall`, from the host, asks whether to take the panel off and whether to take its database and settings with it, then stops the compose project (`down`, with the volumes if you agree), removes the images of the panel if asked, takes the interfaces of the panel, its nftables tables and the ufw rules with its mark off the host, and removes the menu and its short commands from `/usr/local/bin`. The database in `/var/lib/amneziageo-server`, the settings in `/etc/amneziageo-server` and the project directory stay unless you agree to remove them. Docker, the `amneziawg` module and the forwarding file `/etc/sysctl.d/90-amneziageo.conf` stay on the host.
