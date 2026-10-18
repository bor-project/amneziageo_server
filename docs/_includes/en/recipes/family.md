# {{ page.title }}

The task: one person has a phone, a laptop and a tablet, and should get the configurations of all three at one address and then get their changes by themselves. Each device is a client of its own (one configuration on two devices does not work), and one subscription gathers them.

## 1. Subscriptions

"Settings" > "Subscriptions": "Enabled". "Separate port" cleared: the subscriptions answer on the port of the panel under its certificate; or checked with a port like 2096, which is then opened in the firewall with the button under the field. "Profile title": the name the app will see, for example `Family`. "Save" takes effect at once.

## 2. Clients

"Clients" > "Add": `anna-phone`, an interface, the address is filled in. In the "Subscription" field press the button next to it to get a new name, or type your own: Latin letters, digits, dash and underscore, up to 64 characters, case-sensitive. Copy the name. "Save".

The second and third clients, `anna-laptop` and `anna-tablet`: the same name in the "Subscription" field. One name on several clients is one subscription.

## 3. Handing over the subscription

On the "Export" tab of any of the clients the "QR: subscription" card carries the subscription address. The person adds it to the AmneziaGeo app as a subscription address or scans the QR code, and the app takes the configurations of all three clients and then reads the subscription again at the interval from the settings (12 hours by default). The subscription address is a secret: whoever knows it gets the private keys of the clients.

The same address in a browser opens a page with the QR codes of the subscription and of each configuration, the file and the link: handy when the app goes on a device without a camera.

## Further

- A new device: a new client with the same subscription name, and the app picks it up at the next update.
- A disabled client drops out of the subscription, and the app marks its configuration as gone; a subscription all of whose clients are disabled answers empty.
- The traffic limit is counted per client; the `Subscription-Userinfo` header adds up the limits of the clients of the subscription and shows the traffic of today in the app.
- The template of the clients changes `AllowedIPs`, DNS and presets for all of them at once: the subscription assembles the file at every request, see [Templates](../panel/templates.html).
