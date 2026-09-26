# Putting the panel on a server

The panel goes on a server that carries AmneziaWG in the kernel. It takes the clients over, and it raises
the interfaces of the endpoints itself: the key, the port and the obfuscation go into the kernel, the
address ranges onto the interface, the masquerade and the closed ranges into the firewall. A host that has
been raising its interfaces with `awg-quick` hands them over whole.

This page puts the panel on as a package. The same panel goes on as a container, see [docker.md](docker.md).
A guide in Russian that takes a bare Debian 12 to a running panel, step by step, is
[ru/install-debian12.md](ru/install-debian12.md).

## The menu

Every release carries `amneziageo-server`, a script that puts the panel on a bare Debian or Ubuntu host and
looks after it there. As root:

```
curl -fsSL https://github.com/bor-project/amneziageo_server/releases/latest/download/amneziageo-server \
  -o /usr/local/bin/amneziageo-server
chmod 755 /usr/local/bin/amneziageo-server
amneziageo-server install
```

`install` looks the host over first and prints what it carries: the system and the kernel, the upgrades waiting,
the module of AmneziaWG, memory and disk, Docker and its containers, forwarding and BBR, ufw, the ports taken and the
services behind them, the interfaces of AmneziaWG and WireGuard, the certificates of Let's Encrypt. It stops on what
the panel cannot live with: a release missing for the architecture, a container host without the module. Then it asks
everything at once, the answers of the look as defaults, prints the plan and, once told to go on, upgrades the
system, rebooting when a new kernel comes and going on with the same answers when run again, puts on the module from
the PPA of Amnezia when the kernel lacks it, turns forwarding and BBR on, checks the release against the key the
releases are signed with and puts it on as a package or a container, makes the first administrator with a temporary
password, gets a certificate of Let's Encrypt for the name of the server or takes the one the host holds for it, opens
the panel on every address, makes the first endpoint and its first client through the API, opens the ports in ufw or
turns ufw on with ssh and what already listens kept open, and checks the panel, the certificate and the endpoint. The
configuration of the client goes to `/root`, and to the terminal as a QR code when asked. The package puts the script
into `/usr/local/bin` with every release it installs. Given a backup a panel downloaded, it starts the panel on that
database instead of making an administrator and an endpoint, see [Moving to another server](#moving-to-another-server).

`install --answers <file>` takes the answers from a file of `key=value` lines and asks only for what it lacks; with
every key the installation runs without a question:

| Key | Answer |
|---|---|
| `upgrade` | `yes` upgrades the system first |
| `kind` | `package` or `docker` |
| `channel` | `stable` or `test` |
| `restore` | a backup to put the panel on, empty for a fresh panel |
| `bbr` | `yes` turns BBR on |
| `docker_restart` | `yes` restarts Docker with its containers while it drops forwarded packets |
| `login` | the first administrator |
| `generate` | `yes` makes up a temporary password and prints it once, `no` asks for one |
| `domain` | the name of the server for a certificate of Let's Encrypt, empty for none |
| `email`, `terms` | the email for Let's Encrypt, empty for none; `yes` agrees to its terms |
| `port` | the TCP port of the panel |
| `everywhere` | `yes` opens the panel on every address without a certificate |
| `endpoint` | `yes` makes the first endpoint of `endpoint_name`, `endpoint_port`, `endpoint_host` and `websocket` |
| `client`, `qr` | the name of the first client, empty for none; `yes` shows its configuration as a QR code |
| `ufw` | `yes` opens the ports in an active ufw or turns ufw on |
| `reboot` | `yes` reboots after an upgrade that brings a kernel |
| `go` | `yes` goes on past the plan |

Without arguments the script opens a menu, over ssh as well: updates and the way back, copies of the database,
the address, the port and the path of the panel, users and tokens, the service and its log, certificates, the
firewall, endpoints, websocket fronts and BBR. Its items go as commands too:

| Command | Does |
|---|---|
| `install` | looks the host over, asks what the panel needs and puts it on |
| `install --restore <file>` | puts the panel on the host on a backup a panel downloaded |
| `install --answers <file>` | puts the panel on the host with the answers of a file |
| `restore <file>` | puts a backup a panel downloaded in place of the database |
| `update`, `update beta` | moves the panel to the newest release of its channel, of the test channel for `beta` |
| `rollback` | goes back to the release before |
| `uninstall` | takes the panel off the host, its database and settings when asked to |
| `settings` | what the panel answers under, its endpoints and what the host carries |
| `start`, `stop`, `restart`, `status`, `log` | the service |
| `enable`, `disable` | whether the panel starts with the host |
| `bbr on`, `bbr off` | BBR with the `fq` queue for TCP, now and at boot |

Any other command goes to the console of the panel, as in `amneziageo-server user list` or
`amneziageo-server panel show`; `amneziageo-server help` lists them.

The rest of this page puts the panel on by hand.

## Build the package

On the machine the code lives on:

```
./deploy/publish.sh
```

It builds the panel, publishes the server and the console for `linux-x64` with the runtime inside them and
writes `out/amneziageo-server.tar.gz`, about 50 MB. The server needs no .NET installed. `./deploy/publish.sh --ui`
builds the web interface alone into `out/amneziageo-web.tar.gz`, see [Only the web interface](#only-the-web-interface).

A package is a release named after the time it was built and the commit it was built from, as in
`20260911-201500-b0c285f`.

## Put it on the host

```
scp out/amneziageo-server.tar.gz root@<host>:/root/
ssh root@<host>
tar xzf /root/amneziageo-server.tar.gz -C /root
/root/amneziageo-server/install.sh
```

The script puts the release under `/opt/amneziageo-server`, makes `/var/lib/amneziageo-server` for the database
and the geo files, `/etc/amneziageo-server` for the signing key, and installs the service. It does not start
it: the first administrator comes first.

```
/opt/amneziageo-server/AmneziaGeo.Server.Cli init
systemctl start amneziageo-server
```

## Reach it

The service listens on `127.0.0.1:8443` alone, so the panel is not on the network of the host:

```
ssh -N -L 8443:127.0.0.1:8443 root@<host>
```

Then `http://localhost:8443/`. The address, the port, the path and the domain move from the **Settings**
page of the panel afterwards, see [serving.md](serving.md).

## Under a certificate

`/etc/amneziageo-server/server.env` is read by the service and holds what the panel starts under. Naming a
certificate there puts the panel on the network of the host over TLS:

```
Web__Listen__0=*:8443
Web__Certificate=/etc/letsencrypt/live/<host>/fullchain.pem
Web__CertificateKey=/etc/letsencrypt/live/<host>/privkey.pem
```

`systemctl restart amneziageo-server` takes the change. The chain itself is read again whenever it is
renewed on disk. The panel names the same certificate by a pair of paths, on the **Settings** page.

## Take the clients the host carries

For every interface of the host:

```
cd /opt/amneziageo-server
./AmneziaGeo.Server.Cli import endpoint /etc/amnezia/amneziawg/awg1.conf --host <the address clients use>
./AmneziaGeo.Server.Cli import clients /root/awg-clients.json --endpoint awg1 --v6 <the prefix of the host>
```

When the host keeps no file of its own, the peers of the interface file carry the names and the addresses:

```
./AmneziaGeo.Server.Cli import peers /etc/amnezia/amneziawg/awg1.conf --endpoint awg1
```

Such a client carries no private key, so the panel manages it but hands out no file for it; a client that
needs a file of its own is made anew.

Then open the panel and press the button that puts the clients on the host: it answers with what every
interface took and with the peers the panel does not hold.

## Taking the interfaces over from awg-quick

Read the endpoints and the clients out of the interface files first, and check them in the panel. Then stop
the old units and start the service:

```
systemctl disable --now awg-quick@awg0 awg-quick@awg1
systemctl start amneziageo-server
```

The endpoints keep the keys, the ports, the addresses and the obfuscation the files carried, so clients come
back on their own with the files they already hold. What the `PostUp` lines of the old files used to reject
belongs in `Closed to clients` of every endpoint, and what they masqueraded is the NAT switch beside it.

The old files stay where they are, so `systemctl enable --now awg-quick@awg0` brings the host back to what
it was, once the service is stopped.

## Before the panel is the one in charge

Whatever was managing the peers until now has to stop, or the two will undo each other:

| Host | What to stop |
|---|---|
| bor | `/root/awgctl.py`, which rewrites the interface files out of `/root/awg-clients.json` |
| myvpn-ru | `awg-sync.timer` and `xui2kernel.py`, which pour the clients of x-ui into the kernel every minute |

Keep a copy of what the host held before the move: the database of the panel is at
`/var/lib/amneziageo-server/server.db`, and the files it took the clients from are worth keeping as they
were.

## What the service holds

| Path | Holds |
|---|---|
| `/opt/amneziageo-server` | the releases of the server, the console and the panel, see [What the host keeps](#what-the-host-keeps) |
| `/var/lib/amneziageo-server/server.db` | accounts, endpoints, clients, rules |
| `/var/lib/amneziageo-server/geo` | the geo databases, when they are used |
| `/etc/amneziageo-server/signing.pem` | the key the tokens are signed with |
| `/etc/systemd/system/amneziageo-server.service` | the service |

The service runs as root: reading and writing an interface needs `CAP_NET_ADMIN`, and the interface files
belong to root. An account of its own with `AmbientCapabilities=CAP_NET_ADMIN` and the files handed over to
it does the same job.

## Keeping it up to date

The panel puts a newer release on by itself from its `Overview`, see [updates.md](updates.md); it runs the
`install.sh` of the new package the way this section describes, and `amneziageo-server update` asks it for the
same from the shell. By hand, build a new package, copy it over and
run its `install.sh`, the same way as the first time. The interfaces and
their clients live in the kernel and do not go down with the server, so the clients keep their connections
through an update.

### The whole panel

The script puts the new release next to the running one, stops the server, copies the database aside, points
`current` at the new release and starts the server on it. It waits until the server says it is up and watches it
for five more seconds. A release that does not come up, or falls over in that time, is taken back: the copy of
the database returns, `current` points at the release before, the server starts on it, and the script ends with
an error. A server that was stopped before the update stays stopped. A package the host already runs is not put
on again, unless `--force` says so.

What goes on while the server starts over:

| What | Through the update |
|---|---|
| Interfaces and clients | stay in the kernel, with their handshakes and counters |
| Outbounds | keep their server as a peer, with the handshake and the counters; an outbound through a websocket proxy is carried by the server itself and waits for it |
| Routing rules and firewall tables | stay on the host and are laid anew in one step at start |
| Addresses the resolver put into the sets | are read back from the host at start and laid again with the rules |
| The resolver | answers nothing for the seconds the server takes to start; clients ask again |
| Websocket fronts | keep running: a front is started over only when its files change |
| The panel and the subscriptions | do not answer while the server starts |

### Only the web interface

```
./deploy/publish.sh --ui
scp out/amneziageo-web.tar.gz root@<host>:/root/
ssh root@<host>
tar xzf /root/amneziageo-web.tar.gz -C /root
/root/amneziageo-web/install.sh
```

The server keeps running: the script puts the web interface next to the one the server serves and points
`wwwroot` at it, and the next page opened is the new one. A page already open keeps working, since the files it
may still ask for are copied over from the interface before. The package knows the server it was built for and
refuses a host that runs another one: an interface that asks for what the server does not have yet needs the
whole package. `--force` puts it on anyway. `install.sh --ui` of the whole package puts on its web interface
alone, the same way.

### Going back

```
/opt/amneziageo-server/current/install.sh --rollback
```

points `current` back at the release before and starts the server over on it, with the web interface that came
with that release; the same command again returns to the newer one. The database stays as it is: every update copies it aside first, into
`/var/lib/amneziageo-server/backup`, which keeps the last five copies. `install.sh --list` shows the releases
the host keeps. `amneziageo-server rollback` goes back the same way and offers to put a copy of the database back
as well.

### What the host keeps

| Path | Holds |
|---|---|
| `/opt/amneziageo-server/releases/<release>` | a release of the server and the console |
| `/opt/amneziageo-server/web/<release>` | a release of the web interface |
| `/opt/amneziageo-server/current` | the release that runs |
| `/opt/amneziageo-server/previous` | the release before it, where `--rollback` goes |
| `/opt/amneziageo-server/wwwroot` | the web interface the server serves |
| `/var/lib/amneziageo-server/backup` | the database as it was before each of the last five updates |
| `/usr/local/bin/amneziageo-server` | the menu of the release that runs |

The host keeps the current release and the one before it, each with its web interface, and the web interface
served before the last one; the rest goes, a release that did not come up among it. `AmneziaGeo.Server.Api` and `AmneziaGeo.Server.Cli`
in `/opt/amneziageo-server` lead to the current release, so the commands above work as they are.

A panel put on before releases kept its files right in `/opt/amneziageo-server`. The first update moves them
into a release of their own, `legacy-<date>`, and goes on from there.

## Moving to another server

**Download backup** on the **Overview** of the panel, for a role with the right to download backups, saves the
database as it is at that moment: the accounts and tokens, the endpoints with their keys, the clients, the rules
and the settings of the panel. On the new host, as root:

```
amneziageo-server install --restore /root/amneziageo-<name>-<time>.db
```

`install` without the option asks for the file as well. The script checks that the file is a sound database of the
panel, puts the panel on as a package or a container, sets the fresh database aside in
`/var/lib/amneziageo-server/backup` and starts the panel on the backup. On a host that runs the panel already,
`amneziageo-server restore <file>` or **Restore a backup file** in item 5 does the same and sets the database it
replaces aside; a backup of a newer release than the host runs waits until the panel is updated.

The backup keeps the addresses of the old host. The clients reach the server by the name in their configurations,
so the name moves to the new host in DNS and the configurations work on as they are. The certificate of the panel,
the key it signs sign-ins with and the geo files stay out of the backup:

- a certificate the new host lacks comes from Let's Encrypt for the same name when asked, once the name points at
  the new host; otherwise the panel answers without one until item 22 puts it on;
- addresses of the old host the panel listened on drop out;
- the panel downloads the geo sources once it starts.
