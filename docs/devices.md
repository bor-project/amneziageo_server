# Several devices and the second device

## One configuration on two devices

A configuration carries one private key, and the server holds one peer for it: one session and one address it
answers at. Two devices with the same configuration take the peer from each other with every packet, and each
of them stays deaf until it repeats the handshake, 15 seconds on the timers of the kernel.

Measured on the stand, a server and two devices with one key in network namespaces, a ping every 0.2 seconds:

| Case | First device | Second device |
|---|---|---|
| One device | 0% lost | |
| Two devices on one key | 77% lost, gaps of 15 s | 69% lost, gaps of 15 s |
| The second one cut off at the firewall | 0% lost | nothing gets through |
| Cut off and the peer laid anew toward the first | 0% lost from the first packet | nothing gets through |

With two devices the address of the peer changed 27 times in 60 samples half a second apart. One configuration
never serves two devices at once: several devices of one person take a configuration each.

## Several devices

A client with **Several devices** on takes devices. **Add device** in the menu of the client creates one with a
key pair, an address and a subscription of its own, named after the client with a number (`milena-2`), and
opens its configuration. A device takes the interface, the preshared key, the template and the daily limit of its client;
turning the client off or on, changing its template or its limit and removing it carry over to its devices, and
the limit counts the client together with its devices. A device is
listed under its client with its own configuration, traffic and handshake, takes no devices of its own and is
not edited on its own. **Several devices** turns off only once the devices are removed.

| Route | Right |
|---|---|
| `POST /api/clients/{id}/devices` | `clients:write` |

## Online

Every packet of a device, the keepalive included, adds to the counter of its peer and brings the address of the
peer back to it. **Client online for** of the interface, 60 seconds by default, is how long after its last
packet a device counts as online; it takes 10 to 3600 seconds and not less than two keepalive intervals, so a
device that keeps its keepalive on does not drop out between two of them. The list of clients shows a client as
online by it; without the guard, by a handshake within three minutes.

## The second device

The panel reads the peers of the interfaces every two seconds. When the address of a peer moves to a new one and
the old address sends again within **Client online for**, the old device is still online and the new one is a
second device. A device moving from one network to another changes the address once and does not come back. The
old address is kept, the others are cut off:

- the firewall drops what they send to the port of the interface, in the table `inet amneziageo_guard`, by
  address, source port and the port of the interface, so another device behind the same router is not touched;
- the peer is laid anew toward the kept address with a keepalive of 25 seconds for ten seconds, so the kept
  device gets its session back at once, and the keepalive returns to what the peer had;
- the list of clients shows the address that is cut off.

The cut lasts while the kept device is online: once it stays silent longer than **Client online for**, the cut
is lifted and the next device gets in.
The firewall holds an address for three minutes on its own, so a panel that stops leaves nothing behind for
long. Laying the peer anew starts the counters of the interface over.

On the stand, with **Client online for** at 60 seconds, a second device with the configuration of the first was
cut off 3 seconds after it came and the first one lost 2.4 seconds; once the first left, the cut was lifted
63 seconds later and the second one got in a second after.

The guard reads the interfaces and changes the firewall, so it works under root; without the rights it says so
once in the log and stays out. `Guard:IsEnabled` set to `false` turns it off.

## What the application does

An application that knows these routes tells the second device why it is not let in, instead of leaving it
without a handshake. The contract:

1. Every request to the subscription carries `X-Hwid`, a name of the installation that does not change, up to
   128 characters; a random one the application keeps will do.
2. Before it brings up a configuration of a subscription, the application sends
   `POST <subscription address>/hold` with `{"key": "<public key of the configuration>"}` in JSON and `X-Hwid`,
   and repeats it every minute while the configuration is up.
3. When it takes the configuration down, it sends the same request as `DELETE`.

| Answer | Means |
|---|---|
| `200 {"until": <unix>}` | the device holds the configuration until then |
| `409 {"error": "config-busy", "message": "...", "since": <unix>}` | another device holds it: do not connect, show the message; it is in Russian when `Accept-Language` starts with `ru` |
| `400 {"error": "no-device"}` | the request names no device in `X-Hwid` |
| `400 {"error": "bad-key"}` | the body names no public key |
| `404` | the subscription does not carry this configuration |
| `204` | the answer to `DELETE`, the hold is let go |

A hold lasts 150 seconds unless it is repeated, and only the device that holds it lets it go. The holds live in
the memory of the panel, a restart forgets them. The panel answers from the holds alone: a configuration no
application reports stays under the firewall guard above.

## When something is refused

| Code | Means |
|---|---|
| `client-single-device` | the client has **Several devices** off |
| `client-is-device` | a device takes no devices of its own |
| `client-has-devices` | **Several devices** stays on while the client carries devices |
