# {{ page.title }}

A client is one peer of an interface: a key pair, addresses inside the tunnel and the configuration file a device gets. The "Clients" page carries three tabs: "Configs", the clients themselves; [Templates](templates.html), what the clients get in their file; [Routing presets](presets.html), lists for the AmneziaGeo app. One configuration, one device: the server keeps one peer for a key, and two devices with one file take the session from each other, see [One configuration, one device](#one-configuration-one-device).

## List

The "Configs" tab shows the clients of every interface or of one, as chosen with "Every interface" above the list, with a search by name. The "Online only" switch next to them keeps the clients that are online now. The columns:

| Column | What it shows |
|---|---|
| On | the switch of the client; a disabled client is taken off the interface |
| Name | the name, under it "online", "off", "not on the interface" (the peer is not found in the kernel: the interface is down or the panel lacks the rights), "today's limit is used up", "Signal not delivered" |
| Interface | the interface of the client |
| Template | its template or "Default" |
| Address | the addresses of the client in the tunnel |
| Speed | to the client and from the client right now |
| Traffic | of today; with a limit, how much of the limit is used |
| Last online | the time of the last handshake or "never" |
| Actions | the row menu: "Export", "Settings" |

The first column chooses clients, and "Choose every client shown" takes the whole filtered list. The bar above the list counts the chosen ones and carries "Turn on", "Turn off", "Remove"; the result comes as a line "Turned on N of M.", "Not done: <list>." with the reasons and "The interfaces did not take the change: <list>." if the kernel did not take the peers. The list reads the host every two seconds.

"Add" opens a new client. Clients from ready files are moved over by the [import](#import) through the API or the console.

## Client

The page of a client carries two tabs: "Export" and "Settings" (the second one for a role with the "Manage clients" permission).

### Export

Three cards with a QR code: "QR: .conf file", the file of the client; "QR: vpn:// link", the same configuration as an Amnezia link; "QR: subscription", the subscription address, while subscriptions are on (otherwise "Subscriptions are off", "The client has no subscription address" or "The client has no private key"). Each has "Copy the text", "Copy the picture" and "Download". The picture is copied only over HTTPS or from `localhost`, where the browser allows it; the text is copied always. A long `AllowedIPs` list may not fit into the QR code of the file: then the panel opens the card on the link, in which it takes far less room, and if that does not fit either, it writes "Does not fit in a QR code".

The file carries the private key of the client, its addresses, name servers, the packet size and the obfuscation of the interface, and under `[Peer]` the public key of the interface, the preshared key, `AllowedIPs`, `Endpoint` with the host of the interface and keepalive. A client with a template takes `AllowedIPs`, name servers, the packet size and keepalive from the template. An interface without a host gives a file without the `Endpoint` line, and such a client has nowhere to connect. While the resolver of the panel answers at the addresses of the interfaces, the `DNS` line carries the address of the interface of the client instead of public servers, and `AllowedIPs` carries this address too when the ranges leave it out, see [DNS](dns.html). A client of a template with [Route by name](templates.html#route-by-name) gets the resolver of the panel as its only name server and `198.19.0.0/16` in `AllowedIPs`.

The name of the configuration in the file, the link and the subscription is built from the name template on the [Server](server.html#config-name-template) tab, `{HOST}-{INTERFACE}-{CLIENT}` by default.

### Settings

| Field | What it sets |
|---|---|
| Name | up to 64 characters, one of a kind in the whole panel regardless of case |
| Interface | the interface the client goes on; a new client starts on the default interface, see [Interfaces](interfaces.html#general) |
| Address | one address out of each network of the interface; the panel fills in the first free number and hints at a taken one: "Taken by <name>" |
| Private and public keys | the pair of the client, "Generate" makes a new one; the public key is computed from the private one |
| Preshared key | a preshared key of the client's own; empty means the key of the interface |
| Template | the template of the client or "Default" |
| Subscription | the subscription name under which the client is served by link; one name on several clients gathers them into one subscription, empty takes the client out of subscriptions, the button next to it makes a new name, see [Subscriptions](subscriptions.html) |
| Note | a line for yourself, up to 255 characters; it goes into the name of the configuration as the `{NOTE}` substitution |
| Enabled | whether the interface takes the client |
| Access to the client | "As the interface says", "Closed", "The server alone", "The whole tunnel network", see below |
| Allow the server to disconnect the tunnel | whether to send the app the signal to take the tunnel down when the client is disabled, yes by default |
| Daily limit, GB | how much the client may move in a day both ways; empty means no limit |

Removing stands at the bottom of the page: "Remove <name>?". The client is taken off the interface, and the routing rules that named it no longer find it.

## Access to the client

A client is reached from the tunnel only as far as its access allows: "Closed", nobody in the tunnel starts a connection to it; "The server alone", the panel and the host reach it; "The whole tunnel network", all the clients of the interface as well. "As the interface says" takes the "Access to the clients" of the interface, and a new client starts with it. The panel puts this as firewall rules of the interface: the address of the client goes into the set of the interface with the whole network, and the rest towards the clients is dropped. Clients made before this setting appeared got "Closed".

The device on its side lets connections from the tunnel in only when it is told to on the device: in the AmneziaGeo client this is "Allow incoming connections from the VPN network" in the settings of the configuration, or `amneziageo config inbound <name> host` for the server and `network` for the whole network. The file and the `vpn://` link do not carry this. The whole scenario: [Reaching a device behind a client](../recipes/reach-client.html).

## Traffic

The panel reads the counters of each peer every two seconds: the growth of a counter is the traffic of the client, the growth in two seconds is its speed. The traffic of the day is written to the database every 30 seconds and when the panel stops, together with the last readings, so what the peers moved while the panel was not running is counted when it comes back. A day is a day by the clock of the host: at midnight of its time zone the traffic of every client starts from zero.

"Daily limit" is the most a client moves in a day both ways together. When the traffic of the day reaches it, the panel takes the peer off the interface and out of the interface file, and the list writes "today's limit is used up". At midnight, and also when the limit is raised or removed, the peer comes back. The counters are read every two seconds, so a client at full speed goes over the limit by what it managed in that time.

A client is "online" when its peer sent something, keepalive included, within the "Client online for" time of its interface (60 seconds by default), or, if the panel cannot read the interfaces, when its handshake is younger than three minutes.

## Disabling a client

A disabled client leaves the interface, and its device stays with a tunnel nobody answers: the AmneziaGeo app holds such a tunnel so that nothing leaks past it, and a machine reached through the tunnel stays out of reach. So the panel first tells the app to disconnect and then takes the client off the interface. This is how the switch in the list, turning off the chosen clients and saving the settings with "Enabled" cleared work.

The signal goes to an enabled client of an enabled interface that the panel sees connected; nothing is sent to one that is not connected. The AmneziaGeo app for Windows, Linux and Android takes the signal and disconnects as if the user said so: the tunnel does not come up until the user connects again. Another app, including an AmneziaGeo older than the signal, does not take it and stays as it was.

The signal needs both sides. "Allow the server to disconnect the tunnel" on the client is on until it is cleared: with it cleared, hello does not offer the app the signal and the panel does not send it. The app has the same switch in the settings of the configuration, shown while the panel leaves it the choice; an app that turned it off tells the server so at the next hello, and the panel sends such a client no signal.

A signal the app did not take shows in the "Last online" column as "Signal not delivered", with the reason and the time in the hint: "The application did not answer" (no answer in three seconds), "Nothing takes the signal on the device" (the port is closed), "The device was not reached", "What answered is not the application of this client", "The client has no address or key to reach the application". The mark holds until the client is enabled again, removed, or the panel restarts. To send the signal again, enable the client, wait until it connects and disable it. How the signal works is told in the reference of the repository, [services.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/services.md#the-signal-to-disconnect).

## One configuration, one device

A configuration carries one private key, and the server keeps one peer for it: one session and one address to answer to. Two devices with one configuration take the peer from each other with every packet, and each falls silent until it repeats the handshake, that is for 15 seconds; on a test stand two devices with one key lost 69 to 77% of their packets. Every device of a person gets a client of its own, and a subscription gathers the several clients of one person, see [One subscription for several devices](../recipes/family.html).

The guard against a second device is off until the environment of the panel carries `Guard__IsEnabled=true`. When on, it notices a peer taken by two addresses under one key, keeps the device whose turn it is and cuts the others off in the firewall by the source address and port (the table `inet amneziageo_guard`); the list of clients writes "Second device cut off: <list>". The cut holds while the device that was kept is online and is lifted when it is silent longer than "Client online for". The guard reads the interfaces and changes the firewall, so it runs as root; a device that moves between networks is left alone.

## Import

There is no client import button in the panel: clients from ready files are moved over by the console and the API. The commands `amneziageo-server import peers` and `import clients` read files of the host, see [Moving from awg-quick](interfaces.html#moving-from-awg-quick). The route `POST /api/clients/import`, for a role with the "Manage clients" permission, takes `{ configId, text, prefix }`: the number of the interface and the text, either an `awg-quick` interface file whose peers become clients, or a file in which the host kept its clients with private keys, with an IPv6 prefix for the second address. The answer says how many clients were taken (`taken`), how many were skipped as already known by public key (`held`), which ones were taken under another name (`renamed`) and which ones were refused with a code (`refused`), and it puts the interface on the host once. A text with no clients of this interface gets the refusal `bad-client-import`.

## Refusals

| Message | What stands behind it |
|---|---|
| The name of the client does not hold; A client under this name is already there | the name is empty, longer than 64 characters, has forbidden characters or is taken regardless of case |
| The key of the client does not hold; The preshared key does not hold; A client with this key is already there | the keys are not 32 bytes in base64 or the public key is already on another client |
| The address of the client does not hold; The address lies outside the ranges of the interface; This is the network, broadcast or interface address; This address is already taken | addresses |
| The note is too long | more than 255 characters |
| The subscription takes Latin letters, digits, dash and underscore, up to 64 characters | the subscription name |
| The limit is a number of gigabytes above zero, up to 1048576 | the daily limit |
| The access to the client is closed, the server alone, the whole tunnel network or as the interface says | access |
| There is no such client; There is no such interface; There is no such template | a number the panel does not know |
| The interfaces did not take the change | the kernel did not take the peers: the interface is down or the rights are lacking |
