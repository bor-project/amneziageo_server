# {{ page.title }}

The task: behind one of the clients stands a device that must be reached through the tunnel: a home computer, a NAS, a camera. By default the clients of an interface are not reached from the tunnel: only the client itself starts a connection.

## 1. Access on the server

"Clients" > the client `home-nas` > "Settings" > "Access": "Access to the client":

| Value | Who reaches the client |
|---|---|
| Closed | nobody in the tunnel |
| The server alone | the panel and the host of the server: for the disconnect signal, checks and services on the server |
| The whole tunnel network | all the clients of the interface: another client reaches `home-nas` at its address in the tunnel |
| As the interface says | what stands in "Access to the clients" of the interface |

For access from other devices, "The whole tunnel network". To have it so for all the clients of the interface at once, the same is set in "Access to the clients" of the interface, and a single client is left with "As the interface says". The panel puts this as firewall rules of the interface, see [Interfaces](../panel/interfaces.html#what-goes-on-the-host).

## 2. Access on the device

The device itself must also let connections from the tunnel in. In the AmneziaGeo client this is "Allow incoming connections from the VPN network" in the settings of the configuration, or from the console `amneziageo config inbound <name> network` (`host` for the server alone). The configuration file and the `vpn://` link do not carry this: hello passes the app what the server allowed, but the decision stays with the device. The firewall of the device itself must let the needed port in as well.

## 3. Check

From another client of the same interface open the address of `home-nas` in the tunnel, for example `10.8.0.7`, on the needed port. The address is seen in the list of clients. Clients with "Closed" and "The server alone" do not answer this request; the packets to them are dropped on the server.

## What else

- Two clients of one interface reach each other only through the server and its rules; clients of different interfaces do not see each other.
- "The server alone" is needed for the panel to deliver the disconnect signal to a disabled client: the signal goes from the address of the interface to the port of the app inside the tunnel, see [Disabling a client](../panel/clients.html#disabling-a-client).
- The network behind the device (other machines at home) is not reached by this recipe: it needs routes on both sides, which the panel does not set.
