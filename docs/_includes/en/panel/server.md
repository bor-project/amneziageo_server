# {{ page.title }}

The "Server" tab sets where the panel answers and a few settings of the panel as a whole. The address, port and path take effect after "Restart", the language, the name template and pre-releases at once.

## Panel

| Field | What it sets |
|---|---|
| Listen addresses | the addresses of the host on which the panel answers; "Every address", on each address of the host |
| Listen domains | the names the panel answers to; "Any domain", to any. A request with another name gets 404, one from the loopback passes |
| Port | the TCP port of the panel, 8443 until another is set. A port closed by the firewall is named under the field with the "Open" or "Open on save" button; a new port that the firewall closes is not saved until it is opened, see [Ports in the firewall](interfaces.html#ports-in-the-firewall) |
| Path | what comes after the port: `/` is the root, `/panel/` the panel under this path, and everything outside it answers 404 |
| Language | the language the panel opens in until the browser picks its own |
| Config name template | the name under which the configuration of a client goes in the file, the link and the subscription, see below |
| Receive prerelease versions | whether to offer pre-releases of the panel; the panel looks at the releases again right after saving, see [Updates](overview.html#updates) |

{% include shot.html name="server" alt="Server tab: listen addresses and domains, port, path, language, configuration name template with an example, pre-releases" caption="The Server tab of the settings." %}

After a restart that moved the panel, the page moves to the new address by itself: to the new path at the same address, and to the new port and scheme as well, if the page is open on a port the panel listens on.

The panel can also answer on the TCP services port of the interfaces: it is enough to name the same port here. Then hello, the speed test, the WebSocket of the tunnel and subscriptions are answered by the panel itself on this port, no listener of its own is raised for it, and the server answers the world on one TCP port. Such a port always answers over TLS: under the certificate of the panel when there is one, otherwise under a certificate the panel makes itself. Only the path under which the WebSocket of an interface comes on this port is refused: "The path is taken on this port by the WebSocket of an interface".

## First start

While the panel has no settings of its own, what to listen on is told by the list `Web__Listen__<n>` in the environment: `*:8443`, every address of the host; `127.0.0.1:8443`, one address; `[::1]:8443`, an IPv6 address; `lo:8443`, the addresses of the `lo` interface; `wgadmin:8443`, the addresses of a tunnel, so that the panel answers only inside it. {% if page.kind == "docker" %}The image and `compose.yaml`{% else %}The service and `/etc/amneziageo-server/server.env`{% endif %} give `127.0.0.1:8443`. At the first start the list becomes the settings of the panel: the port of the first entry and the addresses of the entries with this port; after that the panel edits them, and the list is read again only if the settings are removed from the database (`amneziageo-server panel reset`).

The path comes together with them: a panel without settings takes `/<16 letters and digits>/`, makes it up once and writes it to the log as the line `the panel answers from *:8443 under /<path>/`; `Web__Path=/` in the environment puts it at the root. The command `amneziageo-server panel set`, given before the first start, writes the same settings first, from the variables it runs under, and changes only what is named. A panel whose address another program holds writes one line `the panel did not start: ... address already in use; free the port or move the panel with 'amneziageo-server panel set --port <port>'` and exits with code 1; {% if page.kind == "docker" %}compose{% else %}systemd{% endif %} starts it again every three seconds until the port is free or the panel moves.

Behind a reverse proxy the panel reads `X-Forwarded-Host`, `X-Forwarded-Proto` and `X-Forwarded-For` only from the addresses in `Web__Proxies__<n>`; this way the subscription of a client carries the name by which the clients reach the server, not an internal address. A name set in the panel is stronger: a subscription takes its own domain, then the domain of the panel, and only then the host of the request.

## Config name template

The name of a configuration in the file, the `vpn://` link and the subscription is built from a template, `{HOST}-{INTERFACE}-{CLIENT}` until another is set. The template is text with substitutions in curly braces, in any case:

| Substitution | What it gives |
|---|---|
| `{HOST}` | the host of the interface, or the host of the request if the interface names none |
| `{INTERFACE}` | the name of the interface |
| `{CLIENT}` | the name of the client |
| `{ID}` | the number of the client |
| `{PORT}` | the port of the interface |
| `{NOTE}` | the note of the client |
| `{DATE}` | the day the client was added, in UTC |

A substitution that came out empty takes the separator after it along (`-`, `_`, `.`, `|` or a space), the name loses separators at its edges, and a name of which nothing is left becomes the moment the client was added, to the millisecond. In a file name the characters `\ / : * ? " < > |` are replaced with `_`. The page shows the name as you type, on the example of the first client; a template with an unknown substitution writes "Unknown substitution: <list>", one longer than 128 characters "The name template is longer than 128 characters".

## Refusals

| Message | What stands behind it |
|---|---|
| The port is outside 1 to 65535; Another program holds the port; The port is taken by <name> | the port |
| The address to listen on was not read; The domain was not read; The path is not one the panel can sit under | addresses, domains, path |
| The path is taken on this port by the WebSocket of an interface | the path on a shared port |
| The panel does not open in this language | the language |
| The name template holds an unknown substitution; The name template is longer than 128 characters | the name template |
| The firewall of the host refused the port | the firewall |
