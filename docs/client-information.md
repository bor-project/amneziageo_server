# Client information

The client page exposes an Info tab and a matching row action. Reading it requires `state:read`. Export and Settings retain `clients:write`; the information response contains no private keys, preshared keys, subscription IDs or access tokens.

## Read API

- `GET /api/clients/{id}/info?days=30` returns the current connection, traffic, limits, effective configuration and statistics availability. Accepted history periods are 7, 30 and 90 days.
- `GET /api/clients/{id}/statistics?from=...&to=...` returns this client's existing connection-log summary. It additionally requires `access:write`. The default period is one hour; an explicit period must be positive and no longer than 90 days.

Both endpoints are read-only. Opening Info does not export a configuration, register a DNS handout, reset a quota, enable connection logging or change client settings.

## Measurement

Peer RX is traffic from the client; peer TX is traffic to the client. Peer totals are kernel counters for the current peer, not lifetime totals. Missing peer counters and unobserved rates are null. Observed zero values remain zero.

Daily traffic uses the existing TrafficStore and TrafficLedger and their server-local accounting day. The response includes that time zone. Quota periods use the UTC boundaries and effective enforcement state from the existing traffic-limit implementation. Configured policy and actual application state are separate values.

The last handshake is not a session start. Last activity is the time the running panel observed a positive counter delta; it is not persisted across panel restarts. Session start is unavailable because the server does not track it.

Effective DNS, AllowedIPs, MTU and keepalive use the same ClientText calculation as configuration export. Sources identify the client, template, interface or built-in defaults. AllowedIPs responses are limited to 1000 entries and include the full count and truncation flag.

## Connection history

Connection logging is the existing optional AccessHost service. Its enablement, runtime state and faults are displayed separately. Daily traffic accounting remains independent of connection logging.

Existing connection-log records have no stable client ID. Per-client statistics therefore match the current tunnel source addresses and interface exactly, with a lower time boundary equal to the latest client creation/update or interface update. The response exposes this boundary as `bindingSince`. Earlier records are excluded to avoid attributing a previous address owner's activity to the current client. Unrelated edits also advance that boundary; historical identity cannot be reconstructed from this log format.

No-connection-history, disabled collection and denied statistics access are separate states. A reader may view Info without receiving connection-log details or export secrets.
