# {{ page.title }}
{% if page.kind == "docker" %}
The panel runs as a container from the image `ghcr.io/bor-project/amneziageo-server` in the network of the host: its tunnel interfaces, firewall rules and ports are the same as the service would have. The host keeps the `amneziawg` module, Docker with compose and the directories with the database and the settings; the panel itself, the utility, the WebSocket proxy and the console menu travel in the image. An update is a new image, a rollback is the previous tag.
{% else %}
The panel runs as the systemd service `amneziageo-server` as root from `/opt/amneziageo-server`: releases lie side by side, the `current` link points at the working one, `previous` at the one before it for a rollback. The database is in `/var/lib/amneziageo-server`, the token signing key and the start variables in `/etc/amneziageo-server`. The WebSocket fronts of the interfaces are the services `amneziageo-proxy@<interface>`.
{% endif %}
## Where to start

1. [Installation](install.html)
2. [First run](first-run.html)

## Console menu

The [console menu](menu.html) `amneziageo-server`: installation, updates and rollback, database backups, the address, port and path of the panel, users and tokens, the service and its log, certificates, the firewall, interfaces, WebSocket fronts, BBR and DNS for the clients.

## Panel

{% include children.html of="panel/index" %}

## Recipes

{% include children.html of="recipes/index" %}

## Utility

The [utility](cli.html): `amneziageo-server <command>` commands for the console and scripts, and the [HTTP API](../api.html) for everything else.
