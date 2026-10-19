# {{ page.title }}

`amneziageo-server` is the script that puts the panel on the host and keeps it running afterwards. It comes with every release{% if page.kind == "docker" %}: on the host it lies in `/usr/local/bin` if you agreed to put the command on the host at the installation, and in the image, from where `docker compose exec panel amneziageo-server` opens it in the project directory{% else %} in `/usr/local/bin`, and every update of the panel updates it{% endif %}. Without arguments it opens the menu, over ssh as well; every item works as a command too, and a command the menu does not know goes to the [utility](cli.html): `amneziageo-server user list`. `amneziageo-server help` prints the list. The short command the installation puts on (`ag-s` by default) is a link to the script in the same directory and opens the same menu.
{% if page.kind == "docker" %}
The menu on the host drives the container from outside with every item, including those that change the host itself; it finds the compose project in the directory named at the installation (`/opt/amneziageo-docker` by default) or by the labels of its container. As it opens, it compares itself with the menu of the release the container runs and, if the two differ, for example after an update with the button in the panel, takes the menu of the release and opens as that one. The menu inside the container does the same except for the items about the host: rollback to an old image, uninstalling and a Let's Encrypt certificate answer that the container does not reach the host, while ufw rules, turning ufw on, BBR and forwarding print the command for the host. Stop, start and restart inside the container hold the panel or start it again without ending the container, so the menu session goes on and a new port or path takes effect without leaving it; autostart turns the restart policy of the container on and off; the log is the log of the container; an update moves the container to a new image and ends the session.
{% endif %}
Under the menu the script prints the status:

| Line | What it shows |
|---|---|
| `Panel` | whether the panel runs, how it is installed, its version and release channel |
| `Autostart` | whether the panel starts with the host |
| `Kernel` | whether the `amneziawg` module is loaded, whether forwarding is on |
| `Websocket` | how many WebSocket fronts run, whether BBR is on |
| `DNS` | the resolver for the clients: off, on, and whether it encrypts the queries going out |
| `Services` | the line of the services of the panel, for example `all 3 work` or `2 of 3 down: subscriptions (port-refused), endpoint awg1 (port-closed udp 443)`, the same as in the "Services" card of the [Overview](panel/overview.html#services) |

While the script gathers these lines, a `reading...` spinner turns in their place, and keys typed meanwhile neither show nor reach the question of the menu. The menu does the same while it reads the settings and lists of its items and submenus.

## Installation and releases

| Item | What it does |
|---|---|
| 1 `Install` | looks the host over, asks for everything needed and installs the panel, see [Installation](install.html); on a host with a panel it answers that item 2 updates it |
| 2 `Update` | moves the panel to the newest release of its channel: asks the panel to update and waits up to five minutes for it to answer with the new version |
| 3 `Update Beta` | the same, with pre-releases |
| 4 `Roll Back` | brings back the previous release, see [Rollback](install.html#rollback), and offers to bring back the copy of the database made before the update |
| 5 `Releases & Backups` | a submenu: releases on the host; database copies; bring a copy back (the panel stops, the former database is put aside next to it); restore a backup file from another panel; make a copy now (the panel stops for a moment); release channel; check for a new release; install a web interface package{% if page.kind == "docker" %} (for the package only){% endif %} |
| 6 `Uninstall` | takes the panel off the host, with its database and settings if asked, see [Uninstalling](install.html#uninstalling) |

## Access and settings of the panel

| Item | What it does |
|---|---|
| 7 `Reset Username & Password` | prints the accounts and sets a password for the one named, enabling it; for a login that does not exist it offers to make an administrator with it{% if page.kind == "package" %}; for a login that belongs to a user of the host it offers to make that user an administrator of the panel, see [System users](panel/users.html#system-users){% endif %} |
| 8 `Users & Roles` | a submenu: users; add; password; role; enable; disable; remove; roles; add a role; permissions of a role; remove a role. These are the `user` and `role` commands of the [utility](cli.html) |
| 9 `API Tokens` | a submenu: tokens; add; revoke; sign in and print the session tokens; who am I: what the panel thinks of the account of the host |
| 10 `Reset Web Base Path` | the path of the panel: a new random one, the root or your own |
| 11 `Reset Settings` | the default address, port, path and certificate of the panel: `127.0.0.1`, 8443, a new random path, no certificate |
| 12 `Change Port` | another TCP port of the panel; a port that is taken is refused; with ufw on it offers to keep the new port open |
| 13 `Listen Addresses & Domains` | listen on every address of the host, on chosen ones (it prints the addresses of the host), or limit the names the panel answers to |
| 14 `View Current Settings` | the panel without secrets: how it is installed, its state and version, the address, the `panel show` settings, interfaces with their ports, the resolver, the module, forwarding, BBR, ufw |

Every change of the address, port, path or certificate restarts the panel and prints its new address. The [Server](panel/server.html) and [Certificates](panel/certificates.html) tabs of the panel do the same.

## Service

| Item | What it does |
|---|---|
| 15 `Start`, 16 `Stop`, 17 `Restart` | {% if page.kind == "docker" %}hold the panel inside the container or start it again; from the host, the whole container{% else %}the `amneziageo-server` service{% endif %} |
| 18 `Check Status` | the {% if page.kind == "docker" %}container{% else %}service{% endif %}, the answer of the panel and its version, autostart; the module (not loaded: it offers to load it now and at boot), forwarding ({% if page.kind == "docker" %}prints the command for the host{% else %}offers to turn it on{% endif %}),{% if page.kind == "docker" %} Docker dropping forwarded packets (prints the `iptables -P FORWARD ACCEPT` that lets them through and the line `"ip-forward-no-drop": true` for `/etc/docker/daemon.json`),{% endif %} the fronts and BBR, the services line. As a command: `amneziageo-server status` |
| 19 `Logs Management` | a submenu: follow the log of the panel; the last 200 lines; the log of the last update; the logs of the WebSocket fronts. As a command: `amneziageo-server log` |
| 20 `Enable Autostart`, 21 `Disable Autostart` | {% if page.kind == "docker" %}the restart policy of the container, `unless-stopped` or `no`{% else %}`systemctl enable` and `disable` of the service{% endif %} |

## Certificates, firewall, SSH

**22 `SSL Certificate Management`.** A submenu: get a Let's Encrypt certificate; renew the certificates now; certificates; put certificate files on the panel; take the certificate off the panel. Getting one asks for the name of the host, checks that the name resolves and TCP 80 is free (`certbot` answers the Let's Encrypt check on it), installs `certbot`, opens 80 in ufw, gets a certificate with an RSA key (a certificate of the same name with an ECDSA key is issued again) and enables the renewal timer `certbot.timer`, then puts the certificate on the panel and offers to listen on every address of the host. "Certificates" prints `certbot certificates` and what the panel answers with.{% if page.kind == "docker" %} Getting and renewing run from the host; the menu gives the container the `/etc/letsencrypt` directory through `compose.override.yaml` and restarts it.{% endif %} More in [Certificates](panel/certificates.html).

**23 `Firewall Management`.** A submenu:

| Sub-item | What it does |
|---|---|
| 1 | the state of ufw, the nftables tables of the panel and the ports it needs: ssh, the panel, subscriptions, interfaces, 80 for renewing the certificate; the interfaces whose clients ufw keeps from the resolver |
| 2, 3 | keep the port of the panel open or leave it to the host |
| 4, 5 | the same for the subscription port |
| 6, 7 | open or close the ports of an interface: the UDP port, the TCP services port and both directions of forwarding through the interface |
| 8, 9 | open or close any port in ufw, like `443/tcp` |
| 10 | turn ufw on: installs it, lets the ssh port in, turns it on and restarts the panel so that it puts its rules |
| 11 | turn ufw off |
| 12 | the nftables tables of the panel as the host holds them: `amneziageo_in` (NAT and access to clients), `amneziageo_rt` (routing), `amneziageo_out` (channels), `amneziageo_open` (open ports without ufw), `amneziageo_guard` (a second device) |
| 13 | `Let the clients reach the resolver`: let the clients of the interfaces ufw keeps from the resolver of the panel (port 53) reach it, with the rule `allow in on <interface> to any port 53`; with the resolver off it says that item 29 turns it on{% if page.kind == "docker" %}; from the container it prints the commands for the host{% endif %}. As a command: `amneziageo-server dns open`, see [DNS](panel/dns.html#how-it-works) |

The panel itself never turns ufw on or off. How it keeps ports open: [Ports in the firewall](panel/interfaces.html#ports-in-the-firewall).

**24 `SSH Port Forwarding Management`.** Keep the panel on `127.0.0.1` (prints the ssh command for a tunnel), let it onto every address, show the ssh command.

## Interfaces, fronts, BBR

**25 `Endpoints & Interfaces`.** A submenu: the interfaces of the panel with their ports and who keeps each port open (`endpoint list`); open or close the ports of an interface; interfaces in the kernel (`device list`); an interface with its peers (`device show`); import an interface from an `awg-quick` file with an address for the clients; import the peers of an interface; import clients from a file of the host; make an interface the default one (`endpoint default`), the one a new client goes on. Moving over is described in [Interfaces](panel/interfaces.html#moving-from-awg-quick).

**26 `Websocket Fronts`.** Interfaces and the ports of their WebSocket{% if page.kind == "package" %} with the `amneziageo-proxy@<interface>` services{% endif %}, restarting the fronts, their logs.

**27 `Enable BBR`.** Prints the congestion algorithm and the queue discipline and offers to turn BBR with the `fq` queue on or off: now and at boot (`/etc/sysctl.d/91-amneziageo-bbr.conf`, `tcp_bbr` in `/etc/modules-load.d`).{% if page.kind == "docker" %} From the container it prints the commands for the host.{% endif %} As a command: `amneziageo-server bbr on` and `bbr off`.

## Geo and DNS

**28 `Update Geo Files`.** Downloads the enabled geo sources of the panel now, like "Update all" on the [Geo lists](panel/geo.html) page.

**29 `DNS for Clients`.** Prints the settings of the resolver (`dns show`) and offers to turn it on or off; if the upstream servers are asked in plain DNS, it offers to replace them with those a fresh panel starts with, over HTTPS. When the resolver is on and ufw keeps the clients of some interfaces from it, the item offers to let them in, as sub-item 13 of item 23 does. As a command: `amneziageo-server dns on`, `dns off`, `dns` with a question, `dns open`. More in [DNS](panel/dns.html).

## Commands

| Command | What it does |
|---|---|
| `amneziageo-server` | open the menu |
| `install [--restore <file>] [--answers <file>]` | look the host over, ask for what is needed and install the panel; on a database backup from another panel; with answers from a file |
| `restore <file>` | put a database backup downloaded from a panel in place of the database |
| `update`, `update beta` | move the panel to the newest release of the channel, with pre-releases for `beta` |
| `rollback` | go back to the previous release |
| `uninstall` | take the panel off the host together with the short commands of the menu |
| `settings` | what the panel answers with, its interfaces and what the host carries |
| `start`, `stop`, `restart`, `status`, `log` | the service and its log |
| `enable`, `disable` | whether the panel starts with the host |
| `bbr on`, `bbr off` | BBR with the `fq` queue, now and at boot |
| `dns on`, `dns off`, `dns` | the resolver for the clients |
| `dns open` | let the clients of the interfaces ufw keeps from the resolver reach it |
| `<utility command>` | `user`, `role`, `token`, `panel`, `endpoint`, `subscriptions`, `import`, `device`, `family`, `geo`, `dns`, `init`, `login`, `whoami`, `update status`, see [Utility](cli.html) |
| `help` | the list of commands |
