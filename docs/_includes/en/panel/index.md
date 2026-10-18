# {{ page.title }}

The panel is the web interface of the server. It is served from the port and path where the server answers, see [Where the panel answers](../first-run.html#where-the-panel-answers), and does everything through the HTTP API under `/api`: a script calls the same routes, see [HTTP API](../../api.html).

{% include children.html of="panel/index" %}

## Signing in

The sign-in page asks for the name and the password; above the form stand the theme and language switches. Signing in gives a token for ten minutes, which the panel refreshes itself; a session lives up to thirty days and ends after fourteen without a refresh. A password issued by an administrator is changed at the first sign-in on the "Password change" page: the current one, the new one and the new one again. Ten misses lock the account for fifteen minutes. Accounts and roles are made in [Users and roles](users.html).

## Header

On the left the name "AmneziaGeo" leads to "Overview", next to it the breadcrumbs of the page; the browser tab is named after the last link: "Rules · AmneziaGeo". On the right:

- "Services down: N": while at least one service of the server is down, the link leads to the "Services" card of the [Overview](overview.html#services);
- "Restart": appears when saved settings of the "Server" and "Certificates" tabs or of the "DNS" page wait for a restart. The button stops the server, {% if page.kind == "docker" %}the restart policy of compose brings the container up{% else %}systemd starts the service{% endif %} again, and the page waits for the answer and moves to the new address by itself if the panel moved to another path or port;
- the theme icon: each press goes round "Auto", "Light", "Dark"; "Auto" follows the system;
- the account menu: the language ("Auto", English, Русский), "Password change", "Sign out". The theme and the language are kept in the browser, not in the account, so every browser has its own;
- the status line `server <version>`, or `the server does not answer` until the panel gets through to it.

## Menu

The menu stands on the left; on a narrow screen the "Menu" button opens it over the page: "Overview", "Interfaces", "Clients", the "Routing" group ("Rules", "Basic", "Channels", "Geo lists", "DNS") and the "Settings" group ("Server", "Certificates", "Subscriptions", "Users and roles", "Diagnostics"). A group opens with a click, the group with the open page is open by itself, and the browser remembers which groups were open. The route "Test" is not in the menu: it opens at `/routing/test` and by a link from the routing pages.

The whole "Settings" section is seen only by a role with the "Manage access" permission. The other sections are seen by any role with the "Read the state" permission, but without the permission for a section the name in a row of the list stays plain text, and there is no row menu and no "Add" button.

## Permissions

| Permission | What it opens |
|---|---|
| Read the state | "Overview" and all the lists: interfaces, clients, templates, rules, channels, geo, DNS, the route test |
| Manage clients | clients, their files and subscriptions, templates, routing presets |
| Manage interfaces | interfaces and their ports in the firewall |
| Manage routing | rules, basic lists, channels and groups, geo sources, the resolver |
| Manage access | the "Settings" section: server, certificates, subscriptions, users, roles and tokens, diagnostics and the connection log; restoring from a backup |
| Update the panel | the "Update" button on "Overview" |
| Download backups | the "Download a copy" button on "Overview" |

The built-in `admin` role carries all the permissions, and a new permission of each release goes into it by itself. Roles of your own are made of these permissions, see [Roles](users.html#roles).

## Lists

A list is a table on a wide screen and a card for each row on a narrow one. The width of a column follows the edge of its header as you drag it, a double click on the edge brings the original back; the browser remembers the widths. Above the list stand sorting and search, and in the list of clients also the choice of an interface. The name in a row leads to the page of the item; in the corner of the row stands the row menu with the ways to its pages: "Settings", and for a client also "Export". What an item does (apply an interface, check a channel, refresh a template) lives on its page, and so does removing it.

A list with an order of its own (rules, channels, geo sources) is rearranged with the "Up" and "Down" arrows next to the row menu and by dragging a row, while the list is neither sorted nor filtered. A list in which rows are chosen (clients) shows a bar after the first choice: "Chosen: N", actions on the chosen rows and "Clear the choice".

While the first answer is on its way, the list says "Loading"; when the server does not answer, it names the failure above the rows it managed to show; it says "Nothing found" and "No clients" only when the server answered so. The lists of clients and interfaces and "Overview" reread the host every two seconds.

## Forms

A form is split into parts: "General", "Network", "Keys", "Obfuscation", "Access" and so on. The "Save" and "Cancel" buttons stand at the bottom of the window and do not move away while the form scrolls; while saving goes on, the button says "Saving". The explanation of a field is a question mark next to its label, and the hint shows on hover. Removing stands on the page of the item and asks "Remove <name>?" with the note "This cannot be undone".

On the settings tabs an unsaved edit stays while other pages are opened, and "Cancel" drops it. Settings that take effect after a restart light the "Restart" button in the header after "Save".

A refusal of the server comes as a code, and the form puts it into words in its language under the field or above the buttons: "The port is outside 1 to 65535", "The name is already taken". The refusal codes of each section are listed on its page.

## Closed ports

The port fields on the "Server" and "Subscriptions" tabs and in the form of an enabled interface ask the firewall of the host whether the port is let in. A closed port is named under the field: "Port 8443 is closed in the firewall of the host". There stands the "Open" button for a saved port or "Open on save" for a new one, if the panel can change the firewall and the role has the permission for this section; otherwise the hint says where to open the port by hand: item 23 of the `amneziageo-server` menu{% if page.kind == "docker" %} or `ufw allow <port>` on the host, which the panel in the container does not reach{% endif %}. More in [Ports in the firewall](interfaces.html#ports-in-the-firewall).

## Page addresses

Every tab and page has an address of its own, so a link and a reload open the same place, and the way back leads to the previous tab: `/clients/templates` is the templates, `/clients/7/export` the export of client 7, `/routing/rules/new` a new rule, `/settings/users` the users. The addresses of earlier releases lead to the new ones: `/configs` and `/proxies` to the interfaces, `/rules` to routing, `/outbounds` and `/balancers` to the channels, `/geo` and `/dns` to their routing pages, `/access` to the users, `/connections/...` to the interfaces, clients and templates.

A page of the panel carries its path in the `base` tag, so the interface and `/api` follow the panel under whatever path it stands.
