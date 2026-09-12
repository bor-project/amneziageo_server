# The point a client opens

A client of AmneziaGeo asks the server what it is and what it offers, and measures the channel against it
instead of against a service on the internet. Two routes carry that, and neither asks for an account of the
panel: a client answers with the key of its own configuration.

## Knowing the server

```
GET /api/hello
```

```json
{ "server": "amneziageo", "version": "1.0.3.0", "challenge": "<32 bytes in base64>" }
```

The word `amneziageo` is what tells a client it reached a server of ours. The challenge waits two minutes and
is taken once.

## Proving the key

```
POST /api/hello
{ "key": "<public key of the client>", "challenge": "<the one handed out>", "proof": "<the answer>" }
```

The answer is counted from the private key of the client and the public key of the endpoint, so the key itself
never travels:

```
shared = X25519(client private key, endpoint public key)
proof  = base64(HMAC-SHA256(shared, "amneziageo-hello" + challenge))
```

The server counts the same from its own private key and the public key of the client. A refusal comes back as
`{ "error", "message" }`:

| Code | Means |
|---|---|
| `unknown-peer` | the panel carries no such peer, or the peer or its endpoint is turned off |
| `stale-challenge` | the challenge was not handed out by this server, it was already answered, or it ran out |
| `bad-proof` | the answer does not come from the key of that peer |

What the server answers a client that proved its key:

```json
{
  "server": "amneziageo",
  "version": "1.0.3.0",
  "client": "milena",
  "features": ["subscription", "speed"],
  "subscription": { "url": "https://host:2096/sub/<subscription>", "updateHours": 12 },
  "speed": {
    "down": "https://host:8443/api/speed/down?bytes=25000000&ticket=<pass>",
    "up": "https://host:8443/api/speed/up?ticket=<pass>",
    "limit": 104857600,
    "expires": "2026-09-12T13:47:58Z"
  }
}
```

`features` names what this client is offered, and the object of the same name carries where to go for it. A
feature the server does not offer is left out of the list and its object is null: `subscription` while the
subscriptions are off or the client carries none, see [subscriptions.md](subscriptions.md), and `speed` while
the endpoint of the client does not measure, see [configs.md](configs.md). The addresses carry the host the
request arrived at, so a client that asked inside the tunnel measures inside the tunnel.

## Measuring

```
GET  /api/speed/down?bytes=<count>&ticket=<pass>
POST /api/speed/up?ticket=<pass>
```

`down` hands over that many zero bytes with a `Content-Length`, `up` takes a body and answers
`{ "bytes": <count> }`. The shape is the one the probe of the client already speaks, so the two addresses go
straight into its settings.

| Bound | Value |
|---|---|
| Bytes in one leg | 100 MB, a larger `bytes` is cut down to it, 25 MB when it is not named |
| The pass | five minutes, one per client, a fresh one drops the one before |
| At a time | one leg per pass, a second answers `measuring` with 429 |

A pass that is not ours, or has run out, answers `unknown-ticket` with 403. What a measurement carries goes
through the tunnel like everything else, so it lands in the counters of the client and in its daily limit, see
[clients.md](clients.md).

## Where it answers

The routes live on the port of the panel, so they are reached wherever the panel answers. A request that
arrives on an address of an interface of an endpoint reaches these two routes and nothing else of the panel:
everything else answers 404, so naming an interface among the listen addresses opens the point to the clients
of the tunnel without opening the panel to them, see [serving.md](serving.md).
