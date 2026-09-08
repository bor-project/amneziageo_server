# The clients

A client is one peer of an endpoint. The panel holds its keys, the addresses it carries inside the tunnel
and whether the endpoint takes it. The interface of the endpoint carries the same clients as peers, and the
interface file the host boots from carries them too.

## Rights

| Route | Right |
|---|---|
| `GET /api/clients` | `state:read` |
| `GET /api/clients/{id}` | `state:read` |
| `GET /api/clients/draft` | `clients:write` |
| `GET /api/clients/{id}/config` | `clients:write` |
| `POST /api/clients` | `clients:write` |
| `POST /api/clients/apply` | `clients:write` |
| `PUT /api/clients/{id}` | `clients:write` |
| `POST /api/clients/{id}/switch` | `clients:write` |
| `DELETE /api/clients/{id}` | `clients:write` |

The private key of a client is written out only to a caller that holds `clients:write`.

## The settings of a client

| Setting | Holds |
|---|---|
| Name | what the client is listed under, up to 64 characters |
| Keys | the pair of the client and, when it carries one of its own, a preshared key |
| Addresses | what the client carries inside the tunnel, up to four |
| On | whether the interface takes the client |
| Note | a line of your own, up to 255 characters |

`GET /api/clients/draft?config=<id>` returns a client that is not saved yet: a fresh key pair, a name no
other client of the endpoint carries and the first free address out of every range of the endpoint.

## What the client is handed

`GET /api/clients/{id}/config` returns the file the client connects with: its private key, its addresses,
the name servers, the packet size and the obfuscation of the endpoint, and under `[Peer]` the public key of
the endpoint, the preshared key, the ranges the client routes into the tunnel, the address the endpoint
answers at and the keepalive. A client without a preshared key of its own takes the one of the endpoint.
The panel shows the same file as a QR code and hands it over as a file. The obfuscation goes over whole:
the junk sizes, the packet types with their spans, the special packets, the header protection key, the
padding and the timings, so a client of a 3.1 endpoint carries the same lines the interface does.

An endpoint with no address of its own leaves the `Endpoint` line out, and a client that gets such a file
has nowhere to connect: name the address of the server in the settings of the endpoint first.

## What the host takes

The panel adds and changes the peers of the interface and takes off the ones it turned off or removed. A
peer it does not hold is left where it is, so a host that carries other peers keeps them; `POST
/api/clients/apply` lists them as stale.

The same clients go into the interface file, so a reboot does not lose them. The head of the file, up to the
first `[Peer]`, is kept as it is: addresses, port, obfuscation and the `PostUp` lines that raise the NAT stay
with the host.

| Setting | Holds |
|---|---|
| `Endpoints:Directory` | where the interface files live, `/etc/amnezia/amneziawg` by default |
| `Endpoints:KeepFile` | whether the clients are written into the file at all |

Reading and writing an interface needs `CAP_NET_ADMIN`: under an account without it the panel keeps the
clients and the file, and says the interface refused.

## Taking the clients a host already carries

```
AmneziaGeo.Server.Cli import endpoint /etc/amnezia/amneziawg/awg1.conf --host bor.sytes.net
AmneziaGeo.Server.Cli import peers /etc/amnezia/amneziawg/awg1.conf --endpoint awg1
AmneziaGeo.Server.Cli import clients /root/awg-clients.json --v6 fdcc:ad94:bacf:61a5::cafe
```

`import endpoint` reads the interface file into an endpoint, keys and obfuscation included, and names the
address clients reach it at. `import peers` reads the peers of the same file: names come from the comments
above them, and private keys are not there, so such a client is managed but not handed a file. `import
clients` reads the file a host keeps its clients in, private keys included; `--v6` adds an address of the
second family to every client, the way the host builds it out of the last number of the first one.

A client whose public key the panel already holds is passed over, so an import runs twice without doubling.

## When something is refused

| Code | Means |
|---|---|
| `bad-client-name` | the name is empty, too long or takes letters the rules do not |
| `bad-client-key` | the pair of the client is not 32 bytes in base64 |
| `bad-client-preshared` | the preshared key is not 32 bytes in base64 |
| `bad-client-address` | the client carries no address, too many, or one that is not an address |
| `client-name-taken` | the endpoint already carries a client under this name |
| `client-key-taken` | another client already carries this public key |
| `client-address-taken` | another client of the endpoint already carries this address |
| `unknown-client` | the panel holds no client under this number |
| `unknown-config` | the panel holds no endpoint under this number |

## What the panel shows

The section lists the clients of every endpoint or of one, with the addresses they carry, what the interface
counted for them and when they last completed a handshake. A client the interface does not carry is marked
as such, so a panel that lost `CAP_NET_ADMIN` or an interface that is down is seen at once.
