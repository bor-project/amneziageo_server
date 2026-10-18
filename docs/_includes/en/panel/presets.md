# {{ page.title }}

A routing preset is a routing list of the AmneziaGeo client that is kept on the panel and handed to the app through the hello of the interface. The template of a client names the presets its clients get; routing by them happens on the device, not on the server. The "Routing presets" tab of the "Clients" page lists the presets with the time of the change and the number of templates that hand them out.

## Main

"Name": up to 64 characters, one of a kind in the panel; this is what the list is called on the client. "On by default": the client puts the list in use when it adds it, unless another list is in use already.

## Lists

| Part | What it does |
|---|---|
| Through the VPN | the entries go through the tunnel |
| Directly | the entries go past the tunnel |
| Block | the client drops the entries |
| Everything through the VPN | everything goes through the tunnel but what goes directly |
| All UDP through the tunnel | every UDP packet goes through the tunnel, whatever the lists say: for calls and games that learn their addresses not through DNS |

The entries are written the same way as in a template: `geoip:ru`, `geosite:youtube`, `cidr:10.0.0.0/8`, `domain:example.com`, up to 256 in each part.

## How the app takes a preset

A preset has a permanent identifier, given when it is added, and the time of the last change of the list: of the name, the entries and the switches. Hello hands them out together with the list, and the client knows the list by its identifier:

- a client without such a list adds it, and puts it in use if the preset is on by default and the client routes by no list;
- a client with the list keeps its own; when the preset is newer, the card of the list on the device offers an update, and the owner of the device, accepting it, gets the name, entries and switches of the preset instead of what the list held, while the list stays in use or not, as it was;
- a list the owner removed comes back at the next hello;
- a list added under the name of the preset by an app older than the identifiers gets tied to the preset.

The card of the list on the device shows where it comes from: the name of the configuration of the client, the one the subscription gives. Together with the presets, hello hands out the geo sources of the panel: the client adds the sources it lacks by address and moves a source with the same name and kind to the address of the panel, so that the keys of the preset name lists the client has, see [Geo lists](geo.html). The whole scenario: [Routing on the client device](../recipes/client-routing.html).

## Removing

A preset a template names is not removed: "A template hands the preset out"; first it is taken out of the templates. Clients that already got the list keep it.

## Refusals

| Message | What stands behind it |
|---|---|
| The name of the preset is empty or longer than 64 characters | the name |
| Not a geo key, a network, an address or a domain; A template carries at most 256 entries | the entries |
| There is no such routing preset | a number the panel does not know |
