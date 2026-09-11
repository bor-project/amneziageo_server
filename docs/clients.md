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
| `POST /api/clients/{id}/devices` | `clients:write` |
| `DELETE /api/clients/{id}` | `clients:write` |

The private key of a client is written out only to a caller that holds `clients:write`.

## The settings of a client

| Setting | Holds |
|---|---|
| Name | what the client is listed under, up to 64 characters, one of a kind in the whole panel whatever the case |
| Keys | the pair of the client and, when it carries one of its own, a preshared key |
| Addresses | one out of every range of the endpoint: not its network, broadcast or own address, and not one another client of the endpoint carries |
| On | whether the interface takes the client |
| Note | a line of your own, up to 255 characters |
| Subscription | the subscription that hands the client out, see [subscriptions.md](subscriptions.md) |
| Several devices | whether the client takes devices of its own, each with its keys and address, see [devices.md](devices.md) |
| Daily limit | how many bytes a day the client moves together with its devices; empty for no limit, see [Traffic](#traffic) |

`GET /api/clients/draft` returns a client that is not saved yet: a fresh key pair, a subscription of its own and
a name no other client carries. With `?config=<id>` it also carries the first number free in every range of the
endpoint, as an address out of each.

The panel adds a client to the interface picked in its form, and the address is written as a number: the head
of every range comes from the interface and the number goes into each of them, so a client of an interface with
IPv6 takes the same number in both families. A client whose addresses do not come out of one number is edited as
a list.

## What the client is handed

`GET /api/clients/{id}/config` returns the file the client connects with: its private key, its addresses,
the name servers, the packet size and the obfuscation of the endpoint, and under `[Peer]` the public key of
the endpoint, the preshared key, the ranges the client routes into the tunnel, the address the endpoint
answers at and the keepalive. A client without a preshared key of its own takes the one of the endpoint.
A client with a template takes the ranges, the name servers, the packet size and the keepalive from the
template instead, and the defaults of the panel where the template names none, see [templates.md](templates.md).
The obfuscation goes over whole: the junk sizes, the packet types with their spans, the special packets,
the header protection key, the padding and the timings, so a client of a 3.1 endpoint carries the same
lines the interface does.

The panel shows the same file as a QR code and hands it over as a file. The answer also carries `link`,
the same file as an Amnezia `vpn://` link: the JSON document Amnezia shares configurations in, packed with
zlib and written in base64url. The AmneziaGeo client reads it from a QR as well, the Amnezia application
takes it as a pasted key, and a long list of ranges takes far less room in it than in the file. The panel
draws a QR of the file and of the link, opens on the link when the file does not fit, and says so when
neither fits.

While the subscriptions are on, the answer carries `subscription` too, the address the client reads its
subscription at, and the window draws it as a third QR code, see [subscriptions.md](subscriptions.md).

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
A name another client already carries gets a number after it (`milena-2`), and the import says so. The
addresses are taken as the host has them, without the range checks the panel makes. Every client taken gets a
subscription of its own.

## When something is refused

| Code | Means |
|---|---|
| `bad-client-name` | the name is empty, too long or takes letters the rules do not |
| `bad-client-key` | the pair of the client is not 32 bytes in base64 |
| `bad-client-preshared` | the preshared key is not 32 bytes in base64 |
| `bad-client-address` | the client carries no address, too many, one that is not a single address or two in one range |
| `client-address-outside` | an address lies outside the ranges of the endpoint |
| `client-address-reserved` | an address is the network, the broadcast or the address of the endpoint |
| `client-name-taken` | another client already carries this name, whatever the case |
| `client-key-taken` | another client already carries this public key |
| `client-address-taken` | another client of the endpoint already carries this address |
| `bad-client-subscription` | the subscription takes letters the rules do not or is longer than 64 characters |
| `bad-client-limit` | the daily limit is negative or larger than 2^50 bytes |
| `client-single-device` | the client has **Several devices** off, see [devices.md](devices.md) |
| `client-is-device` | a device takes no devices of its own |
| `client-has-devices` | **Several devices** stays on while the client carries devices |
| `unknown-client` | the panel holds no client under this number |
| `unknown-config` | the panel holds no endpoint under this number |

## What the panel shows

The section lists the clients of every endpoint or of one, with the addresses they carry, whether they are
online, how fast they move bytes now, what they made today and when they last completed a handshake. A client
the interface does not carry is marked as such, so a panel that lost `CAP_NET_ADMIN` or an interface that is
down is seen at once. The list reads the host every 2 seconds.

## Traffic

The panel reads the counters of every peer each 2 seconds. What a counter grew by since the reading before is
the traffic of the client, and that growth over the 2 seconds is its speed. A counter starts over when the peer
or the interface is laid anew; a counter below the one read last is counted whole. The traffic of the day goes
into the database every 30 seconds and when the panel stops, together with the counters read last, so what the
peers moved while the panel was down is counted once it is back.

The day is the day of the clock of the host: at midnight of its time zone the traffic of every client starts
from nothing.

A client is online when its peer sent anything, keepalives included, within the time its interface counts a
client online (see [devices.md](devices.md)), or, with the guard off, when its last handshake is younger than 3
minutes.

**Daily limit** is the most a client moves in a day, both ways together; a client with devices moves it
together with them. Once the traffic of the day reaches it, the panel takes the peers of the client and of its
devices off the interface and out of the interface file, and the list says the limit is used up. At midnight,
or once the limit is raised or cleared, the panel lays them back. The counters are read every 2 seconds, so a
client going at full speed passes the limit by what it moves in that time.

`GET /api/clients` carries the traffic in `state`: `rxRate` and `txRate`, bytes a second taken from and given
to the client; `todayRx` and `todayTx`, what the client made today; `used`, what it made today together with
its devices, or with its client and the other devices of it; `isSpent`, whether `used` reached the limit. The
limit is `dailyLimit` in bytes, 0 for none.
