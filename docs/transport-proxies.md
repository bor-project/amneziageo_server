# Transport proxy contract v1

The decrypted hello publishes `features.proxies` as an object with `version: 1`,
`source` (the persistent UUID of the catalog), and `items` (a complete array of
proxies selected by the authenticated client's template, in template order).
An enabled proxy not selected by that template is not published. A client without
a template receives an empty array. The database migration adds tables and a
template selection column without changing endpoint or client keys and settings.

[Examples](examples/proxy-hello.json) contain zero, one, and multiple proxies,
a CDN public host, two servers reusing id `1`, and both compatibility directions.
Example credentials are placeholders.

## Identity and updates

The catalog key is `(source, id)`. `id` is a decimal proxy row number and stays
unchanged on edits, restarts, host and certificate changes. The source UUID is
persisted in the server database and travels with its backup. A persistent ECDSA
P-256 key identifies that source across the server's WG interfaces. Each verified
hello carries a proof binding the catalog key to that hello's WG issuer. Locally
created entries use a local source with their own stable ids.

`proof` has three base64/text fields: `publicKey` is DER SubjectPublicKeyInfo for
ECDSA P-256, `issuer` is the exact base64 WG public key verified for the current
hello, and `signature` is a 64-byte IEEE P1363 `r || s` ECDSA signature using
SHA-256. Its exact UTF-8 message is the following concatenation, with LF line
separators and no final LF:

```csharp
"amneziageo.proxy-source.v1\n" + sourceUuidLowercaseD + "\n" + issuerWgPublicKeyBase64
```

The signature authenticates the source-to-issuer binding. The complete catalog
items and transport credentials are protected by the existing sealed hello; they
are not independently signed by this proof. One master key serves all interfaces,
clients and templates in one database. Proofs for distinct issuers differ and
cannot be copied between interfaces.

A client checks the UUID, full SPKI import, P-256 curve, 64-byte signature and
exact issuer equality with its independently verified hello WG key. On first
valid proof it pins `(source, publicKey)` through that verified issuer (TOFU).
Another verified issuer can publish the same source only with a valid signature
from the pinned master key. A different master key under the same source is an
identity conflict and must not replace the previous publication or the pin.
TOFU does not establish trust before the first verified hello.

The wire source UUID is retained as the advertised identity. Client references use
separate SHA-256 domains for signed sources, unsigned `(source, WG issuer)` sources,
and legacy WG issuers; the first sixteen digest bytes form their internal UUID.
A wire UUID therefore cannot collide with a local or legacy reference. A valid
signed upgrade migrates references from the same verified issuer without granting
another issuer access to its credentials.

Proof absence is supported for older catalog servers and remains scoped to the
verified WG issuer until upgraded. An absent or malformed proof must never
downgrade an already pinned source, authorize a second issuer, or revoke the
last accepted snapshot. Invalid proofs are reported as catalog failures. Missing
`proxies` retains the separate legacy behavior described below. Identity reset
requires an explicit trust reset; changing an address or TLS certificate does not
reset the catalog identity.

`ProxySources` stores the private key as base64 PKCS8 `IdentityKey`, beside the
source UUID. `IdentityVersion = 0` and an empty key initialize once to version 1
inside the same database write transaction. Version 1 requires a complete PKCS8
P-256 private key. Missing, damaged, trailing-data or wrong-curve keys fail
publication without generating a replacement under the old UUID. Unsupported
versions and inconsistent version 0 rows also fail. Database backups must retain
both the UUID and the private key. The private key never appears in hello.

A successful hello is a complete snapshot for its authenticated configuration.
Clients retain each configuration's publication separately and aggregate entries
from all registered configurations. Removing a proxy from one publication must
not remove it while another publication still includes it. Credentials belong to
the publication/client: they must not be overwritten with another client's
credential when aggregating the display entry. Removing the last publication
withdraws the entry, retaining an explicit selection as a missing reference.
Failed hello, malformed catalogs and unknown contract versions do not revoke the
previous snapshot and must be reported distinctly from an empty valid catalog.

No `proxies` property means a legacy server. `items: []` is an explicit empty
snapshot from a catalog-aware server. A malformed `proxies` property is not a
legacy answer. `updated` records the proxy's last edit; it does not replace
snapshot semantics or change its catalog key.

## Entry

| Field | Meaning |
|---|---|
| `id`, `name`, `transport` | Stable local id, display name, `websocket` or `xhttp` |
| `host`, `port`, `path` | Public entry host, TCP port and one path segment without slashes |
| `tls.serverName` | TLS SNI and certificate identity, normally the public host |
| `tls.pin` | Optional SHA-256 certificate digest as 64 hex digits |
| `auth.scheme`, `auth.credential` | `bearer` for WebSocket or `vless` and a UUID for XHTTP |
| `updated` | UTC timestamp of the last proxy change |
| `xhttp` | `packet-up`, `uplinkHTTPMethod: GET`, `uplinkDataPlacement: header` |

Normal certificate validation is required unless an explicit pin matches. TLS
private keys, origin certificates, transport signing secrets and WG/AWG private
keys never appear in an entry. The public host can point at a CDN; it need not
equal the origin host or the UDP destination. The internal loopback listener and
its allocated port are server-only data and are never published.

## Destination and authorization

The selected proxy receives the UDP host and port from the active VPN
configuration, independently of its publishing server and interface. WebSocket
v1 uses `Authorization: Bearer <credential>` and `X-AmneziaGeo-Target: host:port`
(bracketed IPv6) on its upgrade and the destination in the wstunnel JWT (`r` and `rp`).
The experimental GET carrier passes that UDP destination in the open query.
The server validates proxy authorization before opening a UDP destination.
One proxy can serve multiple destinations, including external hosts.

Transport credentials are derived from a randomly generated 32-byte proxy secret
and the publishing client id with HMAC-SHA256 over `transport-proxy-v1:<id>`.
WebSocket uses unpadded base64url of the digest. VLESS uses the first sixteen
digest bytes as the .NET `Guid` byte layout and its standard hyphenated UUID.
No VPN key participates in the derivation. The runtime must accept credentials
only for enabled clients whose current templates select the enabled proxy;
template removal, client disabling/deletion and secret rotation revoke access
on new connections. Existing connections end when their transport is closed.

## Selection and availability

A configuration stores either no proxy or one `(source, id)` reference. Any
registered source is allowed. Hello updates connection parameters without
changing the explicit selection. Missing, disabled, invalid, unsupported or
unavailable selected proxies cause an explicit connection error; they do not
select another proxy or send traffic directly. Availability is `Unknown` until a
transport-level probe completes, `Available` after TLS and transport authorization
succeed, and `Unavailable` after a classified transport failure. TCP connect
alone is not an availability probe.

The experimental GET availability probe uses authenticated wire version 2.
A signed challenge initializes the server epoch and monotonic time; the subsequent
bodyless GET to `/<path>/_amneziageo/probe/<32-hex-nonce>` binds its target query,
nonce and response to the current credential. The credential is not sent in Bearer
or URL fields. Replies are signed and marked no-store. HTTP/1.1 is used on the
current carrier; plain HTTP and HTTPS are selected explicitly. Public Host and TLS
SNI remain independent. See [HTTP GET component](http-get-component.md) for the
wire protocol. The WebSocket probe retains its existing authorization and transport.

## Compatibility and migration

`features.websocket` keeps its existing `{port,path}` shape and legacy path/token
authorization for old clients. XHTTP is never represented as that feature.
Old clients ignore the additional `proxies` feature. Until standalone migration
is applied, the current WebSocket carrier continues to use the old feature.
The contract addition does not replace or disable that runtime.

A new client reading an old server retains its current legacy WebSocket path,
port, credentials and per-configuration enablement. The catalog migration creates
a stable legacy alias scoped to the verified server and interface; legacy
target restrictions stay bound to that interface and is not advertised as a
general cross-server bearer credential. A new client must not turn UDP into a
proxy connection merely because hello advertises a proxy.

The client retains explicit selections separately from legacy transport settings,
including an explicit no-proxy choice. Updates to MTU, IPv6 or routing do not
replace that choice. A selected withdrawn entry remains a missing reference with
its last public address and source names across restarts. An unused legacy alias
equivalent to a modern WebSocket entry is hidden; an explicitly selected alias
remains available for compatibility.

Portable bundles use schema version 3 for proxy selection and continue to read
versions 1 and 2. A selected local definition includes its credential, as VPN
configuration keys already do. A server selection carries its reference, public
metadata and trust pin without the server-issued transport credential. Importing
an older bundle without a proxy block preserves an existing choice. An imported
server reference stays missing until a verified matching publication supplies it.

The server catalog migration must retain the old endpoint service port/path/TLS
and legacy WebSocket route while old clients use them. It creates standalone
proxy records and template references idempotently, preserving each client's
effective old publication, including the case where one template was used by
multiple interfaces. The template editor must preserve selections omitted by
older API callers. Client migration retains the explicit transport choice and
its legacy alias. Removing obsolete fields/routes requires a separate explicit
compatibility decision; this epic does not remove them.

Persistence and publication foundations are implemented with AG-686. Public CRUD,
legacy record migration and WebSocket runtime routing are delivered in AG-687; selection
editing in AG-688; common client storage and legacy aliases in AG-690; runtime
adapters and UI in AG-691/692. Apple runtime changes remain AG-694.

## Experimental GET carrier

The feature/http-get-proxy prototype retains the catalog xhttp identifier and credential layout, but replaces VLESS/XHTTP with authenticated GET wire v2. It requires the paired experimental client and server. See [HTTP GET component](http-get-component.md). UDP and WebSocket are preserved.
