# {{ page.title }}

If `amneziageo-server install` put the panel on, the administrator, the interface and the first client are already there, and at the end it printed the address of the panel, the login and the temporary password: go on to [signing in](#signing-in). Here is the same path by hand for a panel installed {% if page.kind == "docker" %}with your own `compose.yaml`{% else %}as a service with `install.sh`{% endif %}.

## The first administrator
{% if page.kind == "docker" %}
```
cd /opt/amneziageo-docker
docker compose exec panel amneziageo-server init --user admin
```

The `--user` key is required in the container: the accounts of the host do not reach into it.
{% else %}
```
/opt/amneziageo-server/AmneziaGeo.Server.Cli init --user admin
```

Without `--user` the administrator becomes the account of the host the utility runs as, that is root, with no password: such an account signs in to the browser only with a password of the panel, which `user passwd <login>` sets.
{% endif %}
The utility asks for the password twice. With the `--generate` key it makes up a password itself and prints it once as a line `password: ...`. The password is temporary: at the first sign-in the panel asks to replace it; `--permanent` keeps the password as it is. `init` works only while the panel has no enabled administrator; after that accounts are made in the panel or with `user add`, see [Users and roles](panel/users.html).

The last line of the utility names the port and the path of the panel.{% if page.kind == "service" %} If the service has never run yet, the database has no settings, and the utility says that the log of the first start will name them. Starting the service: `systemctl start amneziageo-server`.{% endif %}

## Where the panel answers

By default the panel listens only on `127.0.0.1:8443` without TLS and cannot be seen in the network of the host. At its first start it makes up a path like `/<16 letters and digits>/`, so that the sign-in cannot be guessed, and writes it to its log and its database:
{% if page.kind == "docker" %}
```
docker compose logs | grep "the panel answers"
```
{% else %}
```
journalctl -u amneziageo-server | grep "the panel answers"
```
{% endif %}
The panel takes its address, port and path from `server.env` only before its first start: at the first start it writes them to the database and reads them from there afterwards, see [Server](panel/server.html#first-start). To put the panel at the root before the first start, add the line `Web__Path=/` to `server.env`; later the path is changed by menu item 10 or the "Server" tab.

The panel can be opened in two ways.

- **Through an SSH tunnel, without a certificate.** On your computer `ssh -N -L 8443:127.0.0.1:8443 root@<server address>`, in the browser `http://localhost:8443/<path>/`.
- **In the network over TLS.** Menu item 22 `SSL Certificate Management` gets a Let's Encrypt certificate for the name of the server (the name must point at the host, TCP 80 must be free) or takes ready files, puts them on the panel and offers to open it on every address of the host; after that the panel answers at `https://<name>:8443/<path>/`. The same by hand: the [Certificates](panel/certificates.html) tab, then [Server](panel/server.html) > "Listen addresses" > "Every address"; or the lines `Web__Listen__0=*:8443`, `Web__Certificate` and `Web__CertificateKey` in `server.env` before the first start.

The port of the panel in the firewall of the host is opened by menu item 23 or the "Open" button under the port field on the "Server" tab, see [Ports in the firewall](panel/interfaces.html#ports-in-the-firewall).

## Signing in

The sign-in page asks for the name and the password; above the form stand the theme and language switches, so that a browser with neither Russian nor English is not left with nothing. The panel asks to replace a temporary password at once: "The password came from an administrator, replace it". Ten misses close the sign-in for fifteen minutes. What you see after signing in: [Panel](panel/index.html).

## The first interface

"Interfaces" > "Add". The panel offers a draft that already fits together: the name `awg0`, UDP port 51820 or the first free one above it, the network `10.8.0.1/24`, MTU 1420, keepalive 25, DNS `1.1.1.1` and `1.0.0.1`, a new key pair and random obfuscation. Check four things.

1. **Host**: the address or name at which the clients find the server; it goes into the `Endpoint` line of the client files. The panel fills in the name of another interface, the domain of the certificate of the panel or the public IPv4 of the host when it knows them; an enabled interface without a host is not saved.
2. **NAT for clients**: on, so that the clients go out to the internet under the address of the host.
3. **Port** and **Services port**: if the firewall of the host closes them, the form says so under the field and offers "Open on save".
4. **WebSocket proxy**: optional, for networks that let only web traffic through; if the proxy does not start, the panel clears the box and names the reason.

"Save" puts the interface into the kernel. The fields in detail: [Interfaces](panel/interfaces.html).

## The first client

"Clients" > "Add": a name, an interface, an address from the network of the interface (the first free one is already filled in), the keys are generated. After "Save" the client opens on the "Export" tab: the `.conf` file, the `vpn://` link and the subscription address, each as a QR code, with copying and downloading. In detail: [Clients](panel/clients.html).

On the device the configuration is taken by the AmneziaGeo client for Windows, Linux and Android, described on [its own site](https://bor-project.github.io/amneziageo/), or by any other AmneziaWG app. One configuration, one device: two devices with one key take the session from each other, so every person gets a client of their own for every device.

## DNS for the clients

A fresh panel keeps the resolver off: the clients get name servers in their configuration and ask them through the tunnel in plain DNS. Menu item 29 `DNS for Clients`, the command `amneziageo-server dns on` or "Answer the clients" on the [DNS](panel/dns.html) page turn the resolver of the panel on: the clients ask the server for names, the server asks upstream servers over HTTPS, and routing rules by domains start to work.

## Check

{% if page.kind == "docker" %}`docker compose ps` shows the container `healthy`{% else %}`systemctl status amneziageo-server` shows `active (running)`{% endif %}; `amneziageo-server status` (menu item 18) prints the service, the answer of the panel and its version, the module, forwarding and the services line. In the panel the "Services" card on the [Overview](panel/overview.html#services) lists the subscriptions, every interface, the resolver and the certificates with what keeps them from working. The interface on the host: `ip -d link show type amneziawg` and `amneziageo-server device list`.
