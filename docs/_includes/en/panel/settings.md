# {{ page.title }}

The "Settings" section is seen by a role with the "Manage access" permission. It gathers what belongs to the panel itself and to the access into it.

{% include children.html of="panel/settings" %}

The "Server" and "Certificates" tabs write their settings with "Save", and they take effect after "Restart", the button in the header that appears while saved settings wait; the language, the config name template and pre-releases take effect at once. "Subscriptions" take effect without a restart. "Cancel" drops what is not saved, and an unsaved edit stays while other pages are opened.

From the console the same is done by items 10 to 14 of the [menu](../menu.html) and the `panel`, `subscriptions`, `user`, `role` and `token` commands of the [utility](../cli.html).
