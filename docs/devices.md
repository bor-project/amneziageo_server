# One configuration, one device

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
never serves two devices at once: every device of a person takes a client of its own.

## Online

Every packet of a device, the keepalive included, adds to the counter of its peer and brings the address of the
peer back to it. **Client online for** of the interface, 60 seconds by default, is how long after its last
packet a device counts as online; it takes 10 to 3600 seconds and not less than two keepalive intervals, so a
device that keeps its keepalive on does not drop out between two of them. The panel reads the peers of the
interfaces every two seconds, and the list of clients shows a client as online by it; a panel that cannot read
the interfaces shows it by a handshake within three minutes.

## The second device

The guard is off unless `Guard:IsEnabled` is `true` (the variable `Guard__IsEnabled`). Off, the panel only hears
the peers for the list of clients: it cuts nothing off, lays no peer anew and takes the table an earlier run left
off the firewall, so two devices with one key go on as in the table above. The rest of this section is the guard
that is on.

A device that goes from one network to another keeps its session: the address of its peer changes and its
handshake does not, however often it comes back, so the guard leaves it alone. Two devices with one key take the
session from each other, and each gets it back only by a handshake of its own.

A turn is an address that takes the peer under a handshake that came sooner after the one before than the
interface renews its keys (**Rekey after**, 120 seconds unless set); a handshake on that schedule belongs to one
device and starts the count over. When a device takes its turn back for the second time within three minutes,
the peer carries two devices; with the 15 seconds above that is 45 to 60 seconds after the second one came. A
device that took another source port in between keeps its turns as long as the other one comes from another
address. The device whose turn it is stays, the others are cut off:

- the firewall drops what they send to the port of the interface, in the table `inet amneziageo_guard`, by
  address, source port and the port of the interface, so another device behind the same router is not touched;
- the peer is laid anew toward the kept address with a keepalive of 25 seconds for ten seconds, so the kept
  device gets its session back at once, and the keepalive returns to what the peer had;
- the list of clients shows the address that is cut off.

An address of the host itself is never cut off: a client that comes through WebSocket is seen at the loopback,
where its port goes to another client later. A second device that takes another source port with every handshake
comes in again each time, since the cut names the port it had. On an interface whose **Rekey after** is shorter
than those 15 seconds every handshake is on the schedule, and the guard cuts nothing.

The cut lasts while the kept device is online: once it stays silent longer than **Client online for**, the cut
is lifted and the next device gets in.
The firewall holds an address for three minutes on its own, so a panel that stops leaves nothing behind for
long. Laying the peer anew starts the counters of the interface over.

On the stand, with **Client online for** at 60 seconds, a second device with the configuration of the first was
cut off 48 seconds after it came, and before that each of the two fell silent for 15 seconds at a time; once the
first left, the cut was lifted 59 seconds later and the second one got in 4 seconds after. A device that went
between two networks every 10 seconds for three minutes kept its session: one of its 818 echoes was lost.

The guard reads the interfaces and changes the firewall, so it works under root; without the rights it says so
once in the log and stays out.

## What an application can do

An application that knows these routes tells the second device why it is not let in, instead of leaving it
without a handshake. The application of AmneziaGeo does not follow them yet: it sends neither `X-Hwid` nor the
hold, so a second device of it meets the firewall guard above alone, where that guard is on. The contract:

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
application reports stays under the firewall guard above, where that guard is on.
