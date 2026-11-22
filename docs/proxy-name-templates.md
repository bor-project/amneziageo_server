# Name templates

The panel's **Settings → Name templates** page contains the existing client
configuration name template and a separate proxy name template. Both use
case-insensitive substitutions in braces and provide a preview from an existing
record. Unknown substitutions and control characters are rejected; a template
contains at most 128 characters.

The proxy template uses:

| Substitution | Value |
| --- | --- |
| `{HOST}` | Public proxy host, or the hello request host for a legacy empty host |
| `{PROXY}` | Stored proxy name |
| `{TRANSPORT}` | `websocket`, `http`, or `quic` |
| `{ID}` | Proxy ID |
| `{PORT}` | Public proxy port |
| `{PATH}` | Proxy path without surrounding slashes |

For example, `{HOST}-{TRANSPORT}-{PROXY}` produces
`cdn.example-http-office` for an HTTP proxy named `office` at `cdn.example`.
An empty substitution drops the following separator using the same rules as
client configuration names. An empty result uses the original proxy name.
Published names are limited to 64 UTF-16 code units for compatibility with
existing clients; truncation preserves surrogate pairs.

The default `{PROXY}` preserves existing labels. The mask changes the `name`
sent in both `proxies` and `proxiesV2` hello catalogs. It does not rename stored
proxy records, alter source/ID, credentials, destination restrictions, listener
configuration, or client template selections. The new label is sent on the next
hello without a server restart. Existing clients consume the same catalog shape.

`GET /api/panel` includes `proxyNameTemplate`.
`PUT /api/panel` accepts the field; omission preserves the saved value for older
callers, and an explicit empty string resets the default.
`GET /api/panel/proxy-names` returns public preview values using the existing
`NameSample` shape. These routes retain the panel's existing permissions:
`state:read` for reading and `access:write` for saving.
