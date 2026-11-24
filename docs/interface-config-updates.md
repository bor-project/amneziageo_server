# Interface configuration updates

The interface editor compares the complete effective configuration of every client. Template overrides and client DNS choices are resolved before comparison. The response includes field names and SHA-256 revisions; it contains no previous or proposed secret values.

When client configurations change, Apply now updates the interface and publishes the new configurations through the existing subscription. Later persists the proposed interface settings without changing the active interface or its subscription output. Pending changes can be applied or discarded from the interface settings and from client cards by an operator with interface-management rights. Applying a pending change affects the whole interface.

The saved interface revision, effective client revisions and pending payload are checked again before applying. A changed interface, replaced draft or changed effective client configuration requires preparing the proposal again. Existing API callers without the optional defer/revision fields retain immediate application.

All exported AWG/WG fields participate, including headers, signature packets, padding, handshake/rekey timers and future configuration fields. Changes to server-only settings do not create client updates. Unknown client fields are conservatively treated as requiring coordinated connection settings.

The subscription hello includes an optional configurationRevision for its requesting client. Older clients ignore it. The subscription link and file format remain unchanged. Applications fetch the current configuration through the subscription; no offline-client acknowledgement blocks an administrator from applying a change. Applying incompatible settings can interrupt clients until they update.

The ConfigDrafts migration adds storage without rewriting existing interfaces, clients or templates. Pending payloads stay in the server database alongside existing configuration secrets. Revisions and field names do not establish a successful VPN handshake.
