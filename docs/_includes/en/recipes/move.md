# {{ page.title }}

The task: move the panel with its clients, interfaces and settings to a new host so that the clients go on connecting with their configurations. A copy of the database carries everything except the certificate, the token signing key and the geo files.

## 1. A copy on the old host

"Overview" > "Backup" > "Download a copy" (the "Download backups" permission). The file `amneziageo-<name>-<time>.db` carries the accounts and tokens, the interfaces with their keys, the clients, templates, rules, channels and the settings of the panel. From the console a copy is taken by item 5 of the menu, sub-item 5, into the `backup` directory next to the database.

## 2. The panel on the new host

Copy the file to the new host and install the panel there:

```
amneziageo-server install --restore /root/amneziageo-<name>-<time>.db
```

The script checks the file, installs the panel {% if page.kind == "docker" %}as a container{% else %}as a package{% endif %}, puts the fresh database aside into `backup` and starts the panel on the copy; it makes no administrator and no interface, since they are already in the copy. If the panel is already on the new host, the same is done by `amneziageo-server restore <file>` or "Restore from a file" on "Overview" (a copy with addresses the host does not have does not go in from the panel, while the console command fits it to the host).

## 3. The name

The clients reach the server by the name in their configurations, the `Endpoint` line. Move the name in DNS to the address of the new host; the configurations of the clients need no change. If `Endpoint` held an address rather than a name, the clients will need new files: the subscription hands them out by itself as soon as the host of the interface is corrected to the new address.

## 4. Certificate and ports

The certificate of the panel is not in the copy. The script offers to get a Let's Encrypt certificate for the same name once the name already points at the new host; otherwise the panel answers without a certificate until item 22 of the menu puts one on. The addresses of the old host the panel listened on are dropped by the script. The ports of the interfaces, the panel and subscriptions the panel kept open, it opens in the firewall of the new host by itself at the start; ufw on the new host is turned on by the installation or item 23 of the menu.{% if page.kind == "docker" %} The panel in the container does not reach the ufw of the host: on a host with ufw on, the ports are opened by item 23 of the menu on the host or `ufw allow <port>`.{% endif %}

## 5. Check

The "Services" card on "Overview": the interfaces came up, the subscriptions and the resolver answer, the certificate is in place. A client connects from its device without edits. The panel downloads the geo sources by itself after the start.

## 6. The old host

Stop the panel on the old host (`amneziageo-server stop` and `disable`), so that clients whose cached name is not updated yet do not go to both servers. The token signing key on the new host is new: the sessions in browsers and the session tokens end, and you sign in again; long-lived tokens for scripts (`agt_...`) live in the database and keep working.
