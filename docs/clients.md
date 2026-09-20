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
| `POST /api/clients/import` | `clients:write` |
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
| Access to the client | what reaches the client from the tunnel: as the interface says, closed, the server alone, or the whole tunnel network, see [The way back](#the-way-back) |
| Networks behind the client | the ranges the client carries behind it, laid into the `AllowedIPs` of its peer |
| Port forwarding | ports of the host carried to a port of the client over tcp or udp |

`GET /api/clients/draft` returns a client that is not saved yet: a fresh key pair, a subscription of its own and
a name no other client carries. It stands on the endpoint `?config=<id>` names; without it, on the endpoint of the
newest client, else on the first one by name, so a panel with one endpoint has it chosen at once. It also carries
the first number free in every range of that endpoint, as an address out of each.

`POST /api/clients` fills in what the body leaves out: with no keys it makes a pair, with a private key alone it
works out the public one, and with no address it takes the first free number of the endpoint. So a caller of its
own needs no more than `{ "name": "...", "configId": <id> }`, and a body that names an endpoint the panel does
not hold is answered with `unknown-config`.

The panel adds a client to the interface picked in its form, and the address is written as a number: the head
of every range comes from the interface and the number goes into each of them, so a client of an interface with
IPv6 takes the same number in both families. A client whose addresses do not come out of one number is edited as
a list.

## The way back

A client is reached back through the tunnel only as far as its access says. Closed means the tunnel starts
nothing towards it, the server alone means the panel and the host reach it while its neighbours do not, and
the whole tunnel network means every client of the endpoint reaches it as well. A client set to follow the
interface takes what the endpoint carries under `Access to the clients`, see [configs.md](configs.md), and
that is what a fresh client starts with; naming an access of its own outweighs the endpoint. The panel lays
this as
firewall rules of the endpoint, see [configs.md](configs.md): the address of a client that takes the whole
network goes into the set of the endpoint, and everything else aimed at the clients is dropped. Clients that
were already held when the panel took this on take the closed setting.

The networks behind a client go into the `AllowedIPs` of its peer, so the host routes them into the tunnel,
and they are reached under the same access as the client itself. The device at the far end passes them on
itself: the panel puts nothing on it.

A port of the host is carried to a client whatever its access says, because naming the port is the
permission: `tcp:2222:22` takes port 2222 of the host to port 22 of the client. One port of the host is
carried once per protocol, and a client that carries addresses of both families takes the port in both.
The request reaches the client under the address of the endpoint in the tunnel, so the client answers back
through the tunnel whatever ranges it routes there, and sees the server rather than the address the request
came from. A client of AmneziaGeo takes it only while it lets connections in from the tunnel; the server
alone is enough (`amneziageo config inbound <name> host`).

The file and the `vpn://` link of the client name what it takes from the tunnel, so the application turns
the flags of its own operating system on without being told twice: the file carries the lines
`# AmneziaGeo Inbound = <off|server|network>` and `# AmneziaGeo Routes = <ranges>`, and the link carries the
same under `amneziageo`. The line `# AmneziaGeo WebSocket = wss://<host>:<port>/<path>` names the websocket
front the client carries its tunnel through, see [proxy.md](proxy.md), and the link carries it as `websocket`.
A client of another application reads these lines as comments and passes them by. A device of a client takes the access of the client it belongs to, while the
networks behind a client and the ports of the host stay with the record that carries them.

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

Over HTTP the same import takes the text instead of the file. `POST /api/clients/import` takes `{ configId, text,
prefix }`: the text is either an interface file, whose peers become clients, or the file a host keeps its clients in,
read from the part named after the endpoint or from its only part, with `prefix` doing what `--v6` does. It answers
with how many clients it took (`taken`), how many it passed over as already held (`held`), which ones it took under
another name (`renamed`) and which ones it refused with the code (`refused`), and puts the endpoint on the host
once. A text with no client of the endpoint is refused as `bad-client-import`. The page of the clients carries the
same import behind `Import` for an account whose role holds `clients:write`.

## When something is refused

| Code | Means |
|---|---|
| `bad-client-name` | the name is empty, too long or takes letters the rules do not |
| `bad-client-key` | the pair of the client is not 32 bytes in base64 |
| `bad-client-preshared` | the preshared key is not 32 bytes in base64 |
| `bad-client-address` | the client carries no address, too many, one that is not a single address or two in one range |
| `bad-client-import` | the text carries neither peers of an interface file nor clients of the endpoint in the file a host keeps them in |
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
