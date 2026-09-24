# The subscriptions

A subscription is an address a client application reads its configurations from and comes back to on its own.
The panel hands the subscriptions out the way x-ui does: every client carries the name of a subscription, and a
subscription carries every client under its name, so one person with several devices reads all of them from one
address.

## Rights

| Route | Right |
|---|---|
| `GET /api/subscription` | `access:write` |
| `PUT /api/subscription` | `access:write` |
| `GET /<path>/<subscription>` | none, the name of the subscription is the secret |
| `POST`, `DELETE /<path>/<subscription>/hold` | none, the device names itself in `X-Hwid`, see [devices.md](devices.md) |

The subscription of a client is written out only to a caller that holds `clients:write`, the same as its
private key: whoever reads a subscription takes the private keys of its clients.

## The settings

The subscriptions are set on the **Subscriptions** tab of **Settings**. `Save` takes hold at once: the panel
binds the port of the subscriptions anew and, when the host refuses it, keeps the settings it had before and
says why. `Cancel` drops what is not saved yet.

| Setting | What it does |
|---|---|
| Enabled | whether the panel hands the subscriptions out, off by default |
| Listen addresses | the addresses of the host the subscriptions bind, empty for every address |
| Listen domains | the names the subscriptions answer to, empty for any; the first one goes into the address a client is handed |
| Port | 2096 by default; the port of the panel puts the subscriptions next to it, under its certificate |
| Open the port in the firewall | whether the panel holds the port of the subscriptions open in the firewall of the host, see [firewall.md](firewall.md) |
| Base path | what follows the port and goes before the path of a subscription, `/sub/` by default |
| Certificate | the chain and the key the subscriptions answer under, empty for the certificate of the panel |
| Update interval | how often a client reads the subscription again, 12 hours by default, 1 to 720 |
| Profile title | the name a client gives the subscription, empty for the host of its address |

On the port of the panel the listen addresses of the subscriptions do not count, and their path may not be
`api`, `assets` or the path of the panel. A port of their own has to be let through the firewall of the host.

With no domain of their own the subscriptions take the domain of the panel, and with neither the host the
request arrived on. Behind a reverse proxy that host is the inner one until the proxy is named in
`Web:Proxies`, see [serving.md](serving.md).

## What a subscription answers

`GET /<path>/<subscription>` answers 404 when no client carries the subscription. Otherwise the body is base64
of one `vpn://` link a line: one per client that is on, holds a private key and belongs to an interface that
is on. The link is the one the configuration window of the client shows, named by the template of the panel with
the host the subscription is read at for an endpoint without an address, see [clients.md](clients.md). The
revision the services offer is taken with the host of the address they hand out, so it matches the answer the
client gets there. A subscription whose clients are all
off answers an empty body, and the client application marks their configurations gone.

| Header | Holds |
|---|---|
| `Subscription-Userinfo` | `upload` and `download`, what the clients made today, a client with devices counted once together with them; `total`, their daily limits added up, 0 when one of them has none; `expire=0` |
| `Profile-Update-Interval` | the update interval in hours |
| `Profile-Title` | the profile title as `base64:`, left out when it is empty |
| `Cache-Control` | `no-store` |

The traffic is the one the panel counts day by day, see [clients.md](clients.md#traffic): it starts from
nothing at midnight of the host and does not start over when a peer or the interface is laid anew.

## The subscription of a client

A new client takes a subscription of its own, 16 Latin letters and digits, and so does a client taken by an
import; the migration gave one to every client the panel held before. The form of the client changes it: the
same name on several clients puts them into one subscription, an empty one takes the client out of the
subscriptions, the button next to the field makes a new one. The name takes Latin letters, digits, dash and
underscore, up to 64, and the case counts.

`GET /api/clients/{id}/config` carries `subscription`, the address the client reads it at, empty while the
subscriptions are off, the client carries none or holds no private key. The configuration window draws it as a QR code next to the
file and the link.

## When something is refused

| Code | Means |
|---|---|
| `bad-port`, `bad-listen`, `bad-domain`, `bad-certificate` | as on the Server tab, see [serving.md](serving.md) |
| `certificate-*`, `certificate-key-*` | the certificate files do not load, as on the Certificates tab |
| `bad-subscription-path` | the path is empty or takes letters the rules do not |
| `subscription-path-taken` | on the port of the panel the path is one the panel answers under |
| `bad-subscription-interval` | the interval is outside 1 to 720 hours |
| `bad-subscription-title` | the title is longer than 128 characters or breaks the line |
| `subscription-port-busy` | another service holds the port |
| `subscription-failed` | the host did not serve the subscriptions for another reason, the log says which |
| `bad-client-subscription` | the subscription of a client takes letters the rules do not or is too long |

A port the host refuses at start is written down in the log, and the tab shows the reason under its buttons.
