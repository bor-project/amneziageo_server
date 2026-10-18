# {{ page.title }}

A subscription is an address from which the app of a client reads its configurations and to which it comes back by itself. Each client carries a subscription name, and a subscription carries all the clients under this name, so one person with several devices reads them from one address. A client without a private key and a disabled client have nothing to give into a subscription.

## Serving

"Save" takes effect at once: the panel raises the subscription port again, and if the host did not give it, keeps the previous settings and writes the reason under the buttons.

| Field | What it sets |
|---|---|
| Enabled | whether the panel serves subscriptions; no by default |
| Separate port | subscriptions on a port of their own; cleared means "The certificate of the panel": subscriptions next to the panel on its port, under its certificate |
| Listen addresses | the addresses of the host for the own port; empty means all. Not counted on the port of the panel |
| Listen domains | the names the subscriptions answer to; the first goes into the address handed to the client |
| Port | 2096 by default. A port closed by the firewall is named under the field with the "Open" or "Open on save" button and is not saved until it is opened, see [Ports in the firewall](interfaces.html#ports-in-the-firewall) |
| Base path | what comes after the port before the subscription name, `/sub/` by default; on the port of the panel it cannot be `api`, `assets` or the path of the panel |
| Certificate | the chain and the key for the own port; empty means the certificate of the panel |
| Update interval, h | how often the client reads the subscription again, 12 by default, 1 to 720 |
| Profile title | the name the client gives the subscription; empty means the host of the address |

Without domains of their own, subscriptions take the domain of the panel, and without it the host the request came to; behind a reverse proxy that is the internal host until the proxy is named in `Web__Proxies`, see [Server](server.html#first-start). Subscriptions also answer on the TCP services port of each interface.

## What a subscription answers

`GET /<path>/<subscription>` returns base64 of `vpn://` links, one per line: a link for each enabled client with a private key on an enabled interface; the name of the configuration in the link is given by the name template on the "Server" tab. A subscription with no client at all answers 404; a subscription all of whose clients are disabled answers with an empty body, and the app marks their configurations as gone. The subscription name is a secret: whoever reads the subscription gets the private keys of its clients.

| Header | What it carries |
|---|---|
| `Subscription-Userinfo` | `upload` and `download`, the traffic of the clients of today; `total`, their daily limits together, 0 if someone has no limit; `expire=0` |
| `Profile-Update-Interval` | the update interval in hours |
| `Profile-Title` | the profile title in base64, if set |

A browser that asks for `text/html` gets a page instead of base64: the profile title, the subscription name, the number of configurations, the update interval and the traffic of today against the limits, and cards as on the "Export" tab of a client: the QR code of the subscription address and two per configuration, the `.conf` file and the `vpn://` link, with copying and saving. The page speaks Russian to a browser that asks for Russian, follows its light or dark scheme and loads nothing from outside. The AmneziaGeo app asks for `text/plain` and gets base64.

## Subscription of a client

A new client gets a subscription of its own: 16 Latin letters and digits; a client from an import gets one too. The "Subscription" field in the settings of a client changes it: one name on several clients gathers them into one subscription, an empty one takes the client out of subscriptions, and the button next to it makes a new name. The name is Latin letters, digits, dash and underscore, up to 64 characters, case-sensitive. The "Export" tab of a client shows the subscription address as a QR code next to the file and the link, while subscriptions are on. The scenario: [One subscription for several devices](../recipes/family.html).

## Refusals

| Message | What stands behind it |
|---|---|
| The port is outside 1 to 65535; Another service holds the port; The subscriptions did not take the port, the log says why | the port |
| The address to listen on was not read; The domain was not read | addresses and domains |
| The path of the subscriptions was not read; This path is already taken on this port | the path on the port of the panel is taken by the panel itself or by the WebSocket of an interface |
| The certificate takes a chain and a key by their paths; and the refusals of certificate files, as on the "Certificates" tab | the certificate |
| The interval is outside 1-720 hours; The title is longer than 128 characters or breaks the line | the interval and the title |
| The subscription takes Latin letters, digits, dash and underscore, up to 64 characters | the subscription name of a client |
| The firewall of the host refused the port | the firewall |
