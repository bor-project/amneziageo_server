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
{
  "key": "<public key of the client>",
  "challenge": "<the one handed out>",
  "nonce": "<32 random bytes of the client in base64>",
  "proof": "<the answer>"
}
```

The answer is counted from the private key of the client and the public key of the endpoint, so the key itself
never travels:

```
shared = X25519(client private key, endpoint public key)
proof  = base64(HMAC-SHA256(shared, "amneziageo-hello" + challenge))
```

The server counts the same from its own private key and the public key of the client. A refusal comes back as
`{ "error", "message" }` with 403, and a body that is not a request as `bad-request` with 400:

| Code | Means |
|---|---|
| `unknown-peer` | the panel carries no such peer, or the peer or its endpoint is turned off |
| `bad-nonce` | the nonce is not 32 bytes in base64 |
| `stale-challenge` | the challenge was not handed out by this server, it was already answered, or it ran out |
| `bad-proof` | the answer does not come from the key of that peer |

## The features

What the server answers a client that proved its key:

```json
{
  "server": "amneziageo",
  "version": "1.0.3.0",
  "client": "milena",
  "features": {
    "speed": {
      "down": "http://10.9.0.1:51820/api/speed/down?bytes=25000000&ticket=<pass>",
      "up": "http://10.9.0.1:51820/api/speed/up?ticket=<pass>",
      "limit": 104857600,
      "expires": "2026-09-12T13:47:58+00:00"
    },
    "subscription": { "url": "https://host:2096/sub/<subscription>", "updateHours": 12 }
  }
}
```

`features` is a dictionary: the key names a feature, the value is the object of its arguments. A feature the
server does not offer this client is not in the dictionary. A client takes what it knows and passes over the
rest, so a new feature needs nothing from the clients that do not know it yet. On the server a feature is an
`IHelloFeature` registered in `HelloServices`.

| Feature | Offered while | Arguments |
|---|---|---|
| `speed` | always | `down`, `up`, `limit`, `expires` |
| `subscription` | the subscriptions are on and the client carries one, see [subscriptions.md](subscriptions.md) | `url`, `updateHours` |

The addresses carry the host and port the request arrived at.

## The countersign of the server

The answer carries the header `Amneziageo-Proof`, counted over the exact bytes of the body:

```
shared            = X25519(endpoint private key, client public key)
Amneziageo-Proof  = base64(HMAC-SHA256(shared, "amneziageo-server" + nonce + body))
```

The client counts the same from its private key and the public key of the endpoint and takes the features only
when the two match. An answer that does not come from the holder of the endpoint key, or that was changed on
the way, or that answers another nonce, is taken as no server of ours.

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

The routes answer over plain HTTP on the address of every interface of an enabled endpoint, at the port the
endpoint takes its packets on: an interface at `10.9.0.1/24` taking UDP on `51820` answers at
`http://10.9.0.1:51820`. TCP and UDP ports do not collide, so the number is shared. No other address of the host
carries the routes, so they are reached from inside the tunnel alone and a scan from outside finds nothing.

`Hello:Port` in `appsettings.json` moves the routes of every interface to one port, and `0`, the default, keeps
the port of each endpoint:

```json
{
  "Hello": {
    "Port": 9443
  }
}
```

The file of a client names these addresses in `# AmneziaGeo Api`, see [clients.md](clients.md), and the client
asks there. A file without the line leaves the client to ask the first address of its subnet at the port of its
`Endpoint`. A port named in the settings of the configuration on the client outranks both.

The listeners follow the endpoints: adding, changing, turning off or removing one binds them again. An
interface that is not up yet is bound all the same and answers once it carries its address, and an address a
program of the host already holds on that port is written down in the log and left out. With the port of the
endpoint opened, the TCP port of the routes is opened on its interface too, see [firewall.md](firewall.md).

The panel on its own port answers 404 to every request that arrives on an address of an interface.
