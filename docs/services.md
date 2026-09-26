# The services of an endpoint

Every endpoint that is turned on answers on a TCP port besides its UDP one. On that port the panel tells a
client what the server offers it (hello), measures the speed of the way to the server and, when the endpoint
says so, takes the tunnel inside a websocket. The services need no settings of their own: they follow the
endpoint.

## The port

The services take the TCP port with the number of the UDP port of the endpoint. `Services port` in the form of
the endpoint (`servicesPort` in `POST /api/configs` and `PUT /api/configs/{id}`) moves them to another port, 0
keeps the number of the endpoint. A port outside 1 to 65535 is refused with `bad-services-port`.

Endpoints share a port: naming the port another endpoint already serves on leaves one listener answering for
all of them, and the key of the client in the token says which endpoint a request belongs to. The form of a new
endpoint offers the port the endpoints already serve on, the port of the endpoint itself when there are none.
Removing an endpoint takes down its own front and leaves the port to the endpoints that stay on it. The panel
answers on the port itself when it holds that port, see [serving.md](serving.md).

The file of a client carries the port only when it was moved, as the line

```
# AmneziaGeo Services = 8446
```

in `[Interface]`; otherwise the client takes the port of the endpoint. Other applications read the line as a
comment. The link and the subscription carry nothing else of the server.

The port answers under TLS: the certificate of the panel when it has one, see [serving.md](serving.md), and a
certificate the panel makes for itself at start when it has none. A client does not check it: the token below
proves the client, and what the server answers is sealed for that client alone.

`Open the port in the firewall` of the endpoint opens the TCP port of the services together with its UDP port,
see [firewall.md](firewall.md).

## The token

A client proves the keys of its file, and every request carries the same four fields:

| Field | Holds |
|---|---|
| `key` | the public key of the client, in base64 |
| `time` | the clock of the client, in seconds since 1970 |
| `nonce` | 16 random bytes, in base64 |
| `proof` | `base64(HMAC-SHA256(shared, "amneziageo-hello\n" + key + "\n" + time + "\n" + nonce))` |

`shared` is X25519 of the private key of the client and the public key of the endpoint, the secret the
endpoint counts from its own private key and the public key of the client. The server takes a token whose time
stands within five minutes of its own clock and takes each nonce once.

## Hello

`POST /api/hello` with the token as its JSON body. The answer is sealed:

```
{ "iv": "<12 bytes in base64>", "data": "<sealed bytes and the tag of 16 bytes, in base64>" }
```

under AES-256-GCM with the key `HKDF-SHA256(shared, salt = the bytes of the nonce, info = "amneziageo-reply")`.
Opened, it reads

```
{ "server": "amneziageo", "version": "0.0.1.0", "client": "<name>", "features": { ... } }
```

and `features` carries what the server offers this client, each under its name:

| Feature | Arguments | Offered when |
|---|---|---|
| `websocket` | `port` | the endpoint takes the tunnel inside a websocket |
| `routing` | `allowed` | always: whether the client may route by its own lists, from the client and its template, see [templates.md](templates.md) |
| `inbound` | `mode`: `server` or `network` | the client lets connections in from the tunnel, see [clients.md](clients.md) |
| `speed` | `inside`, `outside` (each `down` and `up`), `limit`, `expires` | always: where to measure and until when |

A client leaves out a feature it does not know, and takes a feature the answer leaves out as not offered.

A token that does not hold is refused with `{ error, message, time }`:

| Status | Code | Behind it |
|---|---|---|
| 400 | `bad-request` | the body is not a token |
| 403 | `bad-key` | the key is not 32 bytes in base64 |
| 403 | `bad-nonce` | the nonce is not 16 bytes in base64 |
| 403 | `stale-time` | the time stands more than five minutes off; `time` carries the clock of the server |
| 403 | `unknown-peer` | the endpoint carries no such client, the client or the endpoint is off |
| 403 | `bad-proof` | the proof does not come from the keys |
| 403 | `replayed` | the nonce was already taken |

## The measurement

The hello hands out a pass for five minutes, and a new hello of the same client drops the pass it held.
`inside` points at the first address of the interface, reached through the tunnel, `outside` at the host of the
endpoint, or at the host the hello came to when the endpoint names none.

| Route | What it does |
|---|---|
| `GET /api/speed/down?bytes=<n>&ticket=<pass>` | sends `n` bytes, 25 000 000 by default, 100 MiB at most |
| `POST /api/speed/up?ticket=<pass>` | reads the body, 100 MiB at most, and answers `{ bytes }` |

A pass measures one leg at a time: a second one while the first runs is refused with `measuring` (429), a pass
the server does not hold with `unknown-ticket` (403).

## The websocket

`WebSocket proxy` in the form of the endpoint (`webSocket` in the API) lets a network that passes nothing but web
traffic carry the tunnel. The panel then runs one `wstunnel` per endpoint on `127.0.0.1`, at port
`61000 + id % 4000`, whose whitelist lets it reach the UDP port of its own endpoint on the loopback and nothing
else. On a port several endpoints share, the token of the websocket says whose front the upgrade goes to. The
client opens

```
GET /v1/events HTTP/1.1
Sec-WebSocket-Protocol: v1, authorization.bearer.<the token of wstunnel>
Authorization: AmneziaGeo <base64url of the JSON of the token>
```

on the port of the services. The panel checks the token the way hello does, except that a websocket may come
again with the same token within the five minutes: a client that reads its headers from a file dials with one
token until it is written anew. The panel takes the `Authorization` header off and hands the upgrade to the
`wstunnel` of that endpoint. A websocket without a token that holds, under
another path or to an endpoint without `WebSocket` finds nothing (404).

On a host with systemd the front of an endpoint is the service `amneziageo-proxy@<name>`, with its arguments in
`/etc/amneziageo-server/proxy-<name>.env` and its whitelist in `proxy-<name>.yaml`. On a host without it, a
container among them, the panel runs `wstunnel` itself with the same arguments and starts it again three
seconds after it falls over. Turning `WebSocket proxy` off takes the front down and removes its files; the panel
starting over leaves a front whose files did not change alone.

When the front does not come up or another service holds the port of the services, adding, changing or turning on
the endpoint turns its `WebSocket proxy` off and answers `websocket-down` (409) with the reason and the number of
the endpoint; the form shows the reason under the flag.

An outbound of the `ws` kind proves its keys to the front of another AmneziaGeo server the same way, see
[outbounds.md](outbounds.md).

## The parts

| Where | Holds |
|---|---|
| `AmneziaGeo.Server.Core/Crypto/PeerToken.cs` | the token and the seal |
| `AmneziaGeo.Server.Awg/Config/ConfigServices.cs` | the port of the services and of the front |
| `AmneziaGeo.Server.Api/Services/ServiceServer.cs` | the ports and the fronts, laid whenever an endpoint changes |
| `AmneziaGeo.Server.Api/Services/ServiceDesk.cs` | hello, the measurement and the check of a websocket |
| `AmneziaGeo.Server.Api/Services/ServiceFeatures.cs` | the features |
| `AmneziaGeo.Server.Api/Services/FrontRelay.cs` | the websocket handed to the front |
| `AmneziaGeo.Server.Routing/Proxy/ProxyHost.cs` | the files and the service of a front |
