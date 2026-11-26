# Install with Docker Compose

Use the files in [`deploy/compose`](../deploy/compose) to install the panel without `install.sh` or the host menu.
Docker Engine on Linux and Compose 2.30 or newer are required. The image must include the `bootstrap` command;
older published images cannot initialize from this file. Until an image containing this change is published,
use the source build below. `AMNEZIAGEO_TAG` is the numeric container image tag from the release, not the Git tag
starting with `v`.

## Release archive

Each server release includes `amneziageo-server-compose-<version>.zip` in its assets, for stable, beta and test.
Extract it into a private installation directory. It contains `compose.yaml`, environment and bootstrap
examples, English/Russian instructions and `release.json`. The image repository and exact numeric version are
already selected in `.env.example`; the archive is the same for amd64 and arm64. Docker selects the image
architecture. Source-build overrides, working configuration files, databases, passwords and certificates are
excluded. The archive name, size and SHA-256 are recorded in the signed `update.json`, in `compose`.
The archive's documentation links point at the release commit. Installation needs neither Git nor the installer.

## Host preparation

The VPN uses the host network and `NET_ADMIN`. The host supplies the AmneziaWG kernel module, IPv4/IPv6
forwarding and firewall rules. See [the host requirements](docker.md#what-the-host-carries), including IPv6 RA
and Docker's FORWARD policy. These files do not install packages/modules, run a system upgrade, change BBR,
issue/renew ACME certificates, create host commands or reboot the host. Prepare those separately when needed.
The container already contains WS, HTTP GET/POST and QUIC executables. A proxy-only use does not require an AWG
interface, but the panel remains the administration service. Component-specific standalone installers retain
their existing process; this Compose installs the panel.

## First start

Copy the contents of `deploy/compose` into a private installation directory, or work in that directory of a
repository checkout. Do not copy only `compose.yaml`.

```bash
umask 077
cp .env.example .env
cp server.env.example server.env
cp bootstrap.example.json bootstrap.json
mkdir -p secrets data settings endpoints certificates
read -r -s -p 'Initial administrator password: ' password; echo
printf '%s\n' "$password" > secrets/admin_password
unset password
chmod 600 .env server.env bootstrap.json secrets/admin_password
```

Set `AMNEZIAGEO_TAG` in `.env` to the selected image version. Set the administrator name and initial settings
in `bootstrap.json`. The password goes only in `secrets/admin_password`, not in YAML or environment variables.
The file must contain one password line, at least 8 characters (or the configured minimum). Compose mounts the password and bootstrap
configuration as file secrets. Protect the original files on the host too; Compose does not encrypt them.
See [Docker's secret model](https://docs.docker.com/compose/how-tos/use-secrets/).

```bash
docker compose config --quiet
docker compose pull
docker compose up -d --wait --wait-timeout 120
docker compose logs --tail 40 panel
```

The sample listens on `127.0.0.1:8443`, chooses a random panel path and creates no VPN interfaces or clients.
Find the path in the startup log or with `docker compose exec panel amneziageo-server panel get path`.
Use `ssh -N -L 8443:127.0.0.1:8443 root@server` and open `http://127.0.0.1:8443/<path>/`.
`changePassword: true` requires a password change at first sign-in. Accept the terms in the panel at that login.

Bootstrap runs before the API listens. Invalid input stops startup. Settings, interfaces, clients, routing
defaults and the administrator are written in one database transaction: a refusal rolls them back together.
Unknown JSON fields are rejected. Once an administrator exists, bootstrap skips the file entirely. Recreate
the container freely; changes made in the panel, credentials and generated keys are retained. A database with
existing records but no administrator requires explicit recovery with `init`/`user`, rather than automatic
initialization. Keep the two secret files for Compose recreations, even after initialization.

## Validation and refusal

Bootstrap refuses invalid installation input with exit code **78**, before committing initial settings or
starting the API. Errors identify the field and reason, for example `panel.port`, `panel.domains` or
`dns.upstreams[0]`. Malformed JSON, unknown/read-only/duplicate fields, missing required values and unsupported
enum values are rejected. Passwords and key contents are not included in ordinary errors.

Checks include the configured password policy, interface/client keys, enabled-interface hosts, paths shared
by HTTP services, local IP ownership and TCP/UDP port availability. Compatible HTTP services may share a port
with distinct paths. An enabled VPN requires the kernel module and forwarding. Writable state must stay in
the persistent data/settings/interface mounts of the supplied Compose. Recognized runtime booleans, numeric
settings, path aliases and health overrides are also checked.

All A/AAAA answers of a configured service host must match its listener addresses, or this host's addresses
when listening everywhere. A wrong IPv6 answer is rejected even when IPv4 is correct. NAT/reverse-proxy/CDN
addresses must be explicitly declared, and are still compared to DNS:

```json
"validation": {
  "expectedAddresses": {
    "vpn.example.org": ["203.0.113.10", "2001:db8::10"]
  },
  "trustedCertificates": []
}
```

These are examples; use actual addresses. Unused/misspelled host declarations are rejected. This declares the
expected DNS destination; it does not create or verify a NAT rule or CDN origin configuration.

TLS checks include readable PEM/key files, matching keys, validity dates, server-authentication chain and SAN
coverage of the configured domains. Public certificates use the system trust store and the supplied chain,
without downloading intermediate certificates. For a private/self-signed listener certificate, explicitly
list its CA/trust-anchor PEM in `validation.trustedCertificates`. This acknowledges that listener identity
for installation; it does not install a CA into clients or the container's system trust store. Clients still
need their normal explicit trust/pinning. Enabled subscriptions, WS and a local client-update service require
a TLS certificate. DNS upstreams are probed when the resolver is enabled. A remote updates service requires
HTTPS, an access token and a successful authenticated status/writability check using system TLS trust.

To check a fresh installation without committing its settings, stop any panel of this project first, then:

```bash
docker compose stop panel
docker compose run --rm --no-deps --entrypoint /opt/amneziageo-server/AmneziaGeo.Server.Cli \
  panel bootstrap --file /run/secrets/bootstrap.json --check
docker compose up -d --force-recreate --wait --wait-timeout 120
```

After an error, correct the named field/file/DNS record and recreate the container. The normal restart policy
may retry a rejected startup, but never turns a rejection into successful initialization. Do not delete data
directories to retry. `--check` rejects an already initialized database. Normal restarts keep the existing
administrator/database settings and skip bootstrap input; later changes belong in the panel/utility.
Optional defaults remain valid: an empty panel path generates a path, an empty listen list selects all
addresses, and an empty endpoints list creates no VPN interfaces. A dry-run can prepare database schema and
the signing-key file; administrator and initial configuration records are not committed.

## Installation parameters

`.env` controls Docker itself; `bootstrap.json` controls the initial database; `server.env` contains runtime
configuration. The three files have different roles. Do not duplicate persisted panel settings in `server.env`
and expect them to override later changes in the panel.

| `.env` variable | Default / purpose |
|---|---|
| `COMPOSE_PROJECT_NAME` | `amneziageo-server`; container/project identity |
| `AMNEZIAGEO_IMAGE`, `AMNEZIAGEO_TAG` | GHCR repository and required image version |
| `AMNEZIAGEO_RUNTIME_ENV` | `./server.env`; runtime variables, read without `$` interpolation |
| `AMNEZIAGEO_BOOTSTRAP_FILE` | `./bootstrap.json`; initial configuration secret |
| `AMNEZIAGEO_PASSWORD_FILE` | `./secrets/admin_password`; initial administrator password secret |
| `AMNEZIAGEO_DATA_DIR` | `./data`; database, backups, geo data and client releases |
| `AMNEZIAGEO_SETTINGS_DIR` | `./settings`; signing key and proxy runtime files |
| `AMNEZIAGEO_ENDPOINTS_DIR` | `./endpoints`; generated VPN interface files |
| `AMNEZIAGEO_CERTIFICATES_DIR` | `./certificates`; read-only tree mounted at `/etc/letsencrypt` |
| `AMNEZIAGEO_DOCKER_SOCKET` | `/var/run/docker.sock`; enables in-panel container replacement |
| `AMNEZIAGEO_RESTART` | `unless-stopped`; autostart policy |
| `AMNEZIAGEO_LOG_SIZE`, `AMNEZIAGEO_LOG_FILES` | `10m`, `3`; Docker log rotation |
| `TZ` | `Etc/UTC`; container timezone |

All paths are host paths relative to the Compose directory, or absolute paths. Mount the entire Let's Encrypt
tree, including `archive`, when using the symlinks in `live`. Do not mount an empty or nonexistent Docker socket:
remove its volume entry to disable updates from the panel and update manually instead. Docker socket access
allows administration of the host's Docker daemon. The Compose project files remain on the host for the
existing updater; do not move/delete them while updating. See [Docker networking](https://docs.docker.com/compose/how-tos/networking/).

| `bootstrap.json` field | Purpose |
|---|---|
| `user`, `passwordFile` | Required administrator and container path of its password secret |
| `changePassword` | `true`; temporary initial password, `false` for a permanent one |
| `panel` | Listen IPs, domains, port, path, firewall flag, TLS file paths, language, prereleases and naming templates |
| `routingDefaults` | `false` when omitted; restore bundled address groups and client presets |
| `endpoints` | `[]`; initial interface settings and clients, described below |
| `dns` | Optional resolver settings: enabled, port, listen, upstreams, cache, TTL and client-file DNS policy |
| `subscriptions` | Optional subscription enabled/separate/listen/domains/port/path/TLS/update-hours/title settings |
| `clientUpdates` | Optional enabled/source/address/token/directory/port/path/default settings for client releases |

The sample enables routing defaults and disables subscriptions and client updates. Omitted `panel` uses the
existing `Web__Listen__N`/`Web__Path` startup defaults. An empty `panel.path` generates a random path; `/` means
the root. An empty `panel.listen` listens on all addresses. JSON fields use the product models:
[PanelSettings](../amneziageo-server/AmneziaGeo.Server.Core/Panel/PanelSettings.cs),
[DnsSettings](../amneziageo-server/AmneziaGeo.Server.Routing/Dns/DnsSettings.cs),
[SubscriptionSettings](../amneziageo-server/AmneziaGeo.Server.Core/Panel/SubscriptionSettings.cs) and
[ClientUpdateSettings](../amneziageo-server/AmneziaGeo.Server.Core/Panel/ClientUpdateSettings.cs).
Use camelCase property names and enum names such as `Routing`. Normal validation still applies.

`server.env` accepts the existing ASP.NET double-underscore variables. The sample exposes
`Proxy__Enabled` and `Proxy__Isolated`; set the former to `false` to disable proxies. Additional runtime
settings include `Web__Proxies__N`, `Web__Requests`, `Web__Inline`, `Web__CertificateRoot`, `Geo__Path`, logging
levels and `AMNEZIAGEO_HEALTH`. Host login is disabled in containers. Do not move the DB/signing key outside
the persistent mounts or keep credentials in runtime environment values. Configure interfaces, new proxies
and later service changes in the panel as usual.

## HTTPS and first interface

For public HTTPS, mount an existing certificate tree and change `panel` before first startup:

```json
"panel": {
  "listen": [],
  "domains": ["vpn.example.org"],
  "port": 443,
  "path": "my-panel",
  "certificate": "/etc/letsencrypt/live/vpn.example.org/fullchain.pem",
  "certificateKey": "/etc/letsencrypt/live/vpn.example.org/privkey.pem"
}
```

Set `AMNEZIAGEO_CERTIFICATES_DIR=/etc/letsencrypt` for certificates issued on the host. Bootstrap checks the
PEM/key pair and dates; client trust and matching public names still need a suitable certificate. ACME issuance
and renewal remain on the host. Existing certificate hot reload is retained. See the
[Android certificate recipe](https://bor-project.github.io/amneziageo_server/docker/recipes/android-certificate.html).

To create an interface and client initially, replace `endpoints: []` with:

```json
"endpoints": [{
  "amnezia": true,
  "settings": {
    "name": "awg0", "host": "vpn.example.org", "listenPort": 51820,
    "address": ["10.8.0.1/24", "fd84:3::1/64"],
    "dns": ["1.1.1.1", "1.0.0.1"], "allowedIps": ["0.0.0.0/0", "::/0"],
    "mtu": 1400, "keepalive": 25, "nat": true, "isEnabled": true,
    "webSocket": false, "servicesPort": 443
  },
  "clients": [{"name": "phone"}]
}]
```

Interface and client keys are generated when absent, and retained when provided. `amnezia: true` generates
AWG obfuscation if no non-plain obfuscation was supplied. `amnezia: false` uses plain WireGuard headers. Other
settings, including IPv6, NAT, private UDP, MTU, keepalive, handshake timings, signatures and obfuscation, are
the fields of [ServerConfig](../amneziageo-server/AmneziaGeo.Server.Awg/Config/ServerConfig.cs); clients use
[TunnelClient](../amneziageo-server/AmneziaGeo.Server.Awg/Client/TunnelClient.cs). Client addresses are allocated
when absent. IDs are allocated locally. These initial objects do not replace a backup/restore workflow.

## Build, update and backup

From `deploy/compose` in a checkout with submodules, choose a local image and numeric development version:

```bash
AMNEZIAGEO_IMAGE=amneziageo-server AMNEZIAGEO_TAG=local \
AMNEZIAGEO_BUILD_VERSION=0.0.3.22 \
docker compose -f compose.yaml -f compose.build.yaml up -d --build --wait
```

The standalone copied installation needs only `compose.yaml`; the build override requires repository sources.
For a manual update, stop the panel, take a backup, select the next published image version in `.env`, then run
`docker compose pull && docker compose up -d --wait`. Preserve the same project name and data directories.
The existing update button uses the mounted Docker socket and updates `AMNEZIAGEO_TAG` in `.env`.

For the default relative directories, create a consistent stopped backup:

```bash
(
  set -e
  docker compose down
  umask 077
  backup=$(mktemp ./backup.XXXXXXXXXX.tar.gz)
  if ! sudo tar -czf - .env server.env bootstrap.json secrets data settings endpoints > "$backup"; then
    rm -f -- "$backup"
    exit 1
  fi
  docker compose up -d --wait
  printf '%s\n' "$backup"
)
```

Back up the external certificate tree separately. For custom data paths, back up those paths instead. Restore
all data and settings together while stopped; do not restore only a live SQLite file or delete volumes. Stop
with `docker compose down`; this does not delete bind-mounted data. The host has no new systemd service or
menu command. Run the menu with `docker compose exec panel amneziageo-server`.
