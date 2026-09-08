# Putting the panel on a server

The panel goes on a server that already carries AmneziaWG in the kernel and raises its interfaces with
`awg-quick`. It takes the clients over: the interfaces, the addresses, the NAT and the forwarding stay
where they are, and the panel adds and removes peers and writes them into the interface files.

## Build the package

On the machine the code lives on:

```
./deploy/publish.sh
```

It builds the panel, publishes the server and the console for `linux-x64` with the runtime inside them and
writes `out/amneziageo-server.tar.gz`, about 50 MB. The server needs no .NET installed.

## Put it on the host

```
scp out/amneziageo-server.tar.gz root@<host>:/root/
ssh root@<host>
tar xzf /root/amneziageo-server.tar.gz -C /root
/root/amneziageo-server/install.sh
```

The script puts the files in `/opt/amneziageo-server`, makes `/var/lib/amneziageo-server` for the database
and the geo files, `/etc/amneziageo-server` for the signing key, and installs the service. It does not start
it: the first administrator comes first.

```
/opt/amneziageo-server/AmneziaGeo.Server.Cli init
systemctl start amneziageo-server
```

## Reach it

The service listens on `127.0.0.1:5080` alone, so the panel is not on the network of the host:

```
ssh -N -L 5080:127.0.0.1:5080 root@<host>
```

Then `http://localhost:5080/`. To answer inside a tunnel instead, name the interface in the service file:
`Web__Listen__0=awg1:5080`, see [serving.md](serving.md).

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
| `/opt/amneziageo-server` | the server, the console and the panel |
| `/var/lib/amneziageo-server/server.db` | accounts, endpoints, clients, rules |
| `/var/lib/amneziageo-server/geo` | the geo databases, when they are used |
| `/etc/amneziageo-server/signing.pem` | the key the tokens are signed with |
| `/etc/systemd/system/amneziageo-server.service` | the service |

The service runs as root: reading and writing an interface needs `CAP_NET_ADMIN`, and the interface files
belong to root. An account of its own with `AmbientCapabilities=CAP_NET_ADMIN` and the files handed over to
it does the same job.

## Keeping it up to date

Build a new package, copy it over and run `install.sh` again: it stops the service, replaces the files and
leaves the database where it is. Start it back with `systemctl start amneziageo-server`.
