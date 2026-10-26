# The panel in a container

The panel goes on a server in one of two ways: as a package the host keeps under `/opt/amneziageo-server`, see
[install.md](install.md), or as a container. Both do the same job. The container runs in the network of the
host, so the interfaces of the endpoints, the rules of the firewall and the ports the panel opens are those of
the host, as with the package. A guide in Russian that takes a bare Debian 12 to a panel in a container, step by
step, is [ru/install-debian12-docker.md](ru/install-debian12-docker.md). The script `amneziageo-server` puts the
container on a bare host as well, see [The menu](#the-menu).

## What the host carries

- AmneziaWG in the kernel. The container brings the panel alone, and the module stays on the host:
  `modprobe amneziawg`, and `amneziawg` in `/etc/modules-load.d/` for it to come up with the host.
- Docker with compose.
- Forwarding in both families. The container cannot write `/proc/sys` of the host, so the host turns it on, or
  no endpoint comes up and applying one answers `forwarding is off in /proc/sys/...`:

  ```
  printf 'net.ipv4.ip_forward = 1\nnet.ipv6.conf.all.forwarding = 1\n' > /etc/sysctl.d/90-amneziageo.conf
  sysctl --system
  ```

  On a host that takes its IPv6 route from the advertisements of a router, `net/ipv6/conf/<interface>/accept_ra = 2`
  goes first in the same file: once forwarding is on, the kernel drops that route at once and takes no advertisement
  after, unless the interface keeps `accept_ra` at 2.

- A `FORWARD` chain that lets packets through. Docker turns the policy of the chain to `drop` when it starts and
  turns forwarding on itself, and then no client of the tunnel reaches past the host. The daemon keeps its hands
  off the policy with:

  ```
  echo '{ "ip-forward-no-drop": true }' > /etc/docker/daemon.json
  systemctl restart docker
  ```

  The option keeps the drop from coming, and does not lift one already set: `iptables -P FORWARD ACCEPT` does,
  once, and `ip6tables -P FORWARD ACCEPT` for IPv6. An older Docker does not take the option, and forwarding on at
  boot keeps it from setting the drop, since Docker sets it only as it turns forwarding on. The same holds for the
  package on a host that runs Docker.

The container names in its log what of this the host lacks, as it starts.

## Build and start

On the host, in a copy of the repository with its submodules:

```
cd deploy/docker
docker compose up -d --build
docker compose exec panel amneziageo-server init --user <name>
```

`init --user` makes the first administrator, an account of the panel with a password it asks for twice. Without
`--user` it refuses: host accounts do not sign in from a container. Compose builds the image for the platform of the
host; the build takes a few minutes, most of them for `wstunnel`.

## The menu

The container opens the menu of the server itself, however it was put on:

```
docker compose exec panel amneziageo-server
```

The items work inside the container. Stop, start and restart hold the panel or start it over within the container,
which runs on, so the session goes on and a new port or path takes hold without leaving it. Autostart turns the
restart policy of the container on and off, the log is the log of the container read through compose, and a copy of
the database goes back while the panel is held. An update moves the container onto the new image and ends the
session. The commands of the console go through the same name: `docker compose exec panel amneziageo-server user
list`, and so does the switch of the resolver, `docker compose exec panel amneziageo-server dns on`, see
[dns.md](dns.md#turning-the-resolver-on). The items that change the host itself, going back to an older image, taking
the panel off and a certificate of Let's Encrypt, answer that the container does not reach the host. The rules of
ufw, turning ufw on or off, BBR and forwarding print the command for the host instead.

`amneziageo-server install`, see [install.md](install.md#the-menu), puts the container on a host that carries
Docker with its compose plugin when asked for Docker. Where the host lacks Docker, the plugin or a running daemon,
it prints the commands that put them on from the repository of Docker and stops before it changes anything; the same
command goes on once they are on. It asks for the directory of `compose.yaml`, `/opt/amneziageo-docker` by default,
adds `"ip-forward-no-drop": true` to `/etc/docker/daemon.json` where the version of Docker takes it, lifts the drop
Docker put on forwarding where no ufw holds the chain, and writes `compose.yaml` that runs the image of the newest
release of the channel from `ghcr.io/bor-project/amneziageo-server` with directories of the host for the database
and the settings, then makes the first administrator. On a host that ufw guards it gives ufw what the panel cannot
give it from the container: the ports, the clients of the first endpoint through it and their DNS to the server.
It asks whether the command goes on the host, yes by default. With yes the menu of the release goes to
`/usr/local/bin/amneziageo-server` and drives the container from outside with the same items, the ones of the host
among them, and the commands of the utility go into the container through it, `amneziageo-server user list`; it
finds the project in the directory it was given or by the labels of its container, and as it opens it takes the
menu of the release the container runs where the two differ, after an update from the `Overview` of the panel as
well. With no the installation takes the command off `/usr/local/bin`, and the menu opens in the container,
`docker compose -f <directory>/compose.yaml exec panel amneziageo-server`. With yes it asks for a short command of
the menu on a terminal too, `ag-s` by default, see [install.md](install.md#the-menu).

`amneziageo-server install --restore <file>` and `amneziageo-server restore <file>` put a backup a panel downloaded
into the directory of the database while the container is stopped, see
[Moving to another server](install.md#moving-to-another-server).

## Reach it

The panel listens on `127.0.0.1:8443` of the host, as with the package:

```
ssh -N -L 8443:127.0.0.1:8443 root@<host>
```

`server.env` beside `compose.yaml` takes the variables `/etc/amneziageo-server/server.env` takes for the
package. A certificate is read inside the container, so its directory goes into `volumes` as well:

```
      - /etc/letsencrypt:/etc/letsencrypt:ro
```

The certificate needs an RSA key, so that its chain reaches ISRG Root X1, the root every Android since 7.1 holds:
see [A certificate older Android trusts](https://bor-project.github.io/amneziageo_server/docker/recipes/android-certificate.html).

The health check asks `/api/health` at the address the panel writes into `health` beside its database as it
starts, so it follows the panel to another port, a path of its own and TLS. `AMNEZIAGEO_HEALTH` names another
address. Outside its path the panel answers `/api/health` to the loopback alone.

## What the container holds

| Where | Holds |
|---|---|
| volume `data`, `/var/lib/amneziageo-server` | the database and the geo files |
| volume `settings`, `/etc/amneziageo-server` | the signing key and the files of the websocket fronts |
| `/var/run/docker.sock` of the host | the daemon the panel moves itself onto a new image through |
| `/etc/amnezia/amneziawg` of the host | the files of the interfaces |
| `/opt/amneziageo-server` in the image | the server, the console and the web interface |
| `/usr/local/bin/wstunnel` in the image | the websocket tool |
| `/usr/local/share/amneziageo-server/amneziageo-server` in the image | the menu of the release |
| `/usr/local/bin/amneziageo-server` in the image | the same menu, `docker compose exec panel amneziageo-server` |

## Keeping it up to date

The panel moves itself onto the image of a newer release from its `Overview` when `compose.yaml` hands it the
socket of the daemon, see [updates.md](updates.md); `amneziageo-server update` asks it for the same from the
shell. By hand, a version of the panel is a tag of the image:

```
git pull --recurse-submodules
AMNEZIAGEO_TAG=<version> AMNEZIAGEO_VERSION=<version> docker compose up -d --build
```

Compose starts the container over on the new image. The interfaces and their clients live in the kernel and
do not go down with it; the proxies, the resolver, the panel and the subscriptions are away for the seconds
the container takes to start. The database stays in its volume. The update does not copy it aside, so a copy
before the update is taken by hand:

```
docker compose stop panel
docker compose cp panel:/var/lib/amneziageo-server ./backup-<date>
docker compose start panel
```

### Going back

The images of earlier versions stay on the host:

```
AMNEZIAGEO_TAG=<earlier version> docker compose up -d
```

The database stays as the newer panel left it. The copy from before the update goes back into the volume with
`docker compose cp`, while the container is stopped. `amneziageo-server rollback` starts the container on the
newest image of the same name the host keeps below the one it runs, and offers to put a copy of the database back.

## How it differs from the package

| What | In the container |
|---|---|
| Websocket fronts | the panel runs `wstunnel` itself and starts it again three seconds after it falls over; the fronts go down and come up with the container |
| Firewall | the image carries no ufw, so the panel lays its own nftables tables; on a host whose ufw is turned on, its `drop` wins, and the ports are opened in ufw by hand; the tabs of the settings and the forms and the list of the endpoints name such a port as closed; the image carries iptables for nft to show the ports of the rules ufw laid through an older iptables, see [firewall.md](firewall.md) |
| Accounts of the host | turned off: `Auth__HostLogin=Off` and `Auth__HostUsers=false`, the panel signs in its own accounts alone and refuses to carry one to the host |
| Restart from the panel | the server ends, and the restart policy of compose starts the container again |
| The web interface alone | not updated apart: every image carries the server and the interface together |
