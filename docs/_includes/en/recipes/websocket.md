# {{ page.title }}

The task: the client sits in a network where UDP does not pass or only web traffic passes. The interface also carries the tunnel inside WebSocket on its TCP port; the AmneziaGeo app learns this way in from the server, and whether to turn WebSocket on is up to the owner of the device.

## 1. Interface

"Interfaces" > the interface > "Settings":

1. "WebSocket proxy" on.
2. "Services port": the TCP port on which WebSocket, hello and the speed test answer. Empty means the number of the UDP port of the interface. For the tunnel to look like an ordinary site, set 443; several interfaces can share one services port, and the panel can answer on it too, if you name the same port on the [Server](../panel/server.html) tab.
3. "WebSocket path": the part of the address under which WebSocket comes; a new interface has one made up, and there is no need to pick it. The path must not be the same as the path of the panel, of subscriptions or of the WebSocket of another interface on the same port.
4. "Save". If the front did not come up or another program holds the port, the panel clears the box and names the reason under it.

{% if page.kind == "docker" %}The panel itself starts the front inside the container.{% else %}The front is the service `amneziageo-proxy@<interface>`; item 26 of the console menu shows the fronts, restarts them and reads their logs.{% endif %} The TCP services port must be open in the firewall: the "Open" button under the field or item 23 of the menu, sub-item 6.

## 2. Client

Nothing needs changing for the client: the AmneziaGeo app asks the hello of the interface and learns the port and path of WebSocket from it. If the services port is moved, the file of the client carries the line `# AmneziaGeo Services = <port>`, and the client gets a new file from the "Export" tab or through the subscription. On the device WebSocket is turned on with the "Proxy" switch in the transport settings of the configuration, as described on the [client site](https://bor-project.github.io/amneziageo/).

Another AmneziaWG app does not open WebSocket by itself: it needs a `wstunnel` of its own on the device that dials `wss://<host>:<services port>/<path>` and puts a local UDP port in place of the server. A client whose front was written by hand passes the same way: WebSocket lets in by the path, not by the file of the panel.

## 3. Check

The "Services" card on the [Overview](../panel/overview.html#services) shows the "WebSocket" part on the interface; "the WebSocket front does not run", "fell over" or "does not listen" name a trouble with the front, "the firewall of the host keeps <port> closed" one with the firewall. On a device in a network without UDP the tunnel comes up over TCP.

The same port and path take a WebSocket channel of another AmneziaGeo server: there it is a channel of the "WebSocket" type with the proxy `wss://<host>:<services port>/<path>`, see [Exit through another server](another-server.html).
