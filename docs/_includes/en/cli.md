# {{ page.title }}

The utility is the console of the server: it works with the same database as the panel and does what cannot be done over HTTP before the first sign-in, and everything else from scripts as well. {% if page.kind == "docker" %}In the container it is called as `docker compose exec panel amneziageo-server <command>` from the project directory (`/opt/amneziageo-docker` by default); the `amneziageo-server` menu on the host, if the installation put it on the host, passes it the commands it does not know itself: `amneziageo-server user list`.{% else %}It lies in the release as `/opt/amneziageo-server/AmneziaGeo.Server.Cli`, and the `amneziageo-server` menu passes it the commands it does not know itself: `amneziageo-server user list`. A member of the `amneziageo` group runs it without `sudo`; reading the interfaces of the kernel needs root.{% endif %} `amneziageo-server help` prints the commands of the menu, the utility without a command prints its own.

The utility asks for a password in the terminal; `--generate` makes one up and prints it once, `--permanent` does not require a change at the first sign-in. Commands that remove something ask for confirmation, `--yes` answers for you.

## Sign-in and accounts

| Command | What it does |
|---|---|
| `init [--user <login>] [--generate] [--permanent]` | the first administrator, while the panel has no enabled one; without `--user`, the account of the host the utility runs as{% if page.kind == "docker" %} (in the container `--user` is required){% endif %}. Its last line names the port and the path of the panel |
| `login [--user <login>] [--json]` | sign in and print the session tokens |
| `refresh <token>` | exchange a refresh token for a new pair |
| `whoami` | what the panel thinks of the account of the host the utility runs as |
| `user list` | accounts with their role and state |
| `user add <login> [--role <role>] [--host [--key <public key>]] [--generate] [--permanent]` | an account of the panel; `--host` makes it a user of the host as well, with an ssh key |
| `user passwd <login> [--generate] [--permanent]` | a new password |
| `user role <login> <role>` | the role |
| `user enable`, `disable`, `remove <login>` | enable, disable, remove |
| `role list`, `role add <code> [--title <title>] <permission>...`, `role set <code> [--title <title>] [<permission>...]`, `role remove <code> [--yes]` | roles; the permissions are `state:read`, `clients:write`, `interfaces:write`, `routing:write`, `access:write`, `updates:write`, `backup:read` |
| `token list`, `token add <name> --role <role> [--days <n>]`, `token revoke <id> [--yes]` | tokens for scripts; the secret is printed once as the last line |

## Panel

| Command | What it does |
|---|---|
| `panel show` | where the panel answers |
| `panel get port`, `path`, `listen`, `domains`, `certificate`, `key`, `opened`, `channel` | one value, for scripts |
| `panel set [--port <n>] [--path <path> | random | /] [--listen <addresses> | all] [--domains <names> | none] [--certificate <chain> --key <key> | --certificate none] [--opened on | off] [--channel stable | test]` | the settings of the "Server" and "Certificates" tabs; `--opened` keeps the port of the panel open in the firewall, `--channel` is the release channel. Given before the first start, it writes the settings first, see [First start](panel/server.html#first-start) |
| `panel reset [--yes]` | `127.0.0.1`, port 8443, a new random path, no certificate |
| `subscriptions show`, `subscriptions get <name>`, `subscriptions set --opened on | off` | subscriptions: show, one value, keep the port open |
| `update status`, `update check`, `update apply [--beta]` | updates: how the last one went, look at the releases now, install the newest; the menu calls `apply` in items 2 and 3 |
| `backup check <file>` | check a database backup file |

The settings change in the database; the panel takes the address, port, path and certificate after a restart, and the menu restarts it by itself after every `panel set`.

## Interfaces and clients

| Command | What it does |
|---|---|
| `endpoint list` | interfaces with their port and who keeps the port open: the panel or the host |
| `endpoint open <name>`, `endpoint close <name>` | keep the ports of an interface open in the firewall or leave them to the host |
| `import endpoint <file.conf> [--name <name>] [--host <address>] [--dns <list>] [--allowed <list>]` | an interface from an `awg-quick` file, with its keys and obfuscation |
| `import peers <file.conf> [--endpoint <name>]` | the peers of an interface file as clients without private keys |
| `import clients <file.json> [--endpoint <name>] [--v6 <prefix>]` | clients from a file in which the host kept them with private keys |
| `device list` | the `amneziawg` interfaces in the kernel |
| `device show [name] [--secrets]` | an interface with its peers, as the kernel holds it |
| `device set <name> [--key <base64>] [--generate] [--port <n>] [--fwmark <n>] [--shape jc=4,jmin=40,...] [--i1 <spec>] [--hpk <base64>]` | write the settings of an interface into the kernel directly, for debugging |
| `device peer <name> <public key> [--allowed <a,b>] [--endpoint <host:port>] [--preshared <base64>] [--keepalive <s>] [--replace-ips] [--update-only] [--remove]` | a peer of an interface in the kernel, for debugging |
| `family [name]` | the number of the netlink family |

The `device` and `family` commands talk to the kernel past the database of the panel: whatever they changed, the panel puts back by its own settings the next time it applies them. Clients, rules, channels and templates are not edited from the console: that is the [HTTP API](../api.html) with a token from `token add`.

## Geo and DNS

| Command | What it does |
|---|---|
| `geo update` | download the enabled geo sources now |
| `dns show` | the settings of the resolver |
| `dns get enabled`, `port`, `upstreams`, `encrypted`, `outbound` | one value; `encrypted` is `all`, `none` or `partly`: whether the upstream servers are asked encrypted |
| `dns set --enabled on | off [--upstreams <servers> | default]` | turn the resolver on or off, set the upstream servers or bring back those a fresh panel starts with |

The menu commands `amneziageo-server dns on` and `dns off` call the same with questions.

## Example

A token for a day, the list of clients through the API and revoking the token:

```
amneziageo-server token add nightly --role admin --days 1
curl -s -H "Authorization: Bearer agt_..." https://panel.example.com:8443/api/clients
amneziageo-server token revoke <id> --yes
```

Every command takes `--db <file>` to work with another database, for example a copy.
