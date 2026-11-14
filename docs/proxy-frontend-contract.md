# Proxy access contract

AG-779, server implementation. Client application changes are a separate scope.

## Destination selection

The management request and response use `targets`:

| Choice | Stored policy |
| --- | --- |
| Local interfaces only | `{"restricted":true,"local":true,"destinations":[]}` |
| Specified addresses and domains | `{"restricted":true,"local":false,"destinations":[{"host":"vpn.example","port":45120}]}` |
| Any destination | `{"restricted":false,"local":false,"destinations":[]}` |

New proxies default to local-only. Existing records without `local` retain their previous policy. An update with omitted/null `targets` preserves the saved policy. A selected list authorizes exact Host/UDP-port pairs; it does not implicitly include local interfaces. CDN addresses, TLS identities and VPN destination hosts remain independent.

Both hello catalogs add `scope: local|selected|any`. The existing `targets` field contains concrete effective destinations, including expanded enabled interface endpoints for local mode. Filter proxy choices for the VPN endpoint using that list. Do not filter the whole catalog solely against the VPN currently used for hello: a proxy can serve other available configurations. Keep an incompatible saved selection visible as incompatible; do not switch to direct UDP automatically.

## Access tokens

The proxy has an array of independent named access tokens. The identifier in metadata is not the connection secret.

| Method | Route | Body/result |
| --- | --- | --- |
| GET | `/api/proxies/{proxyId}/tokens` | Array of `{id,name,createdUtc}`; no secret |
| POST | `/api/proxies/{proxyId}/tokens` | `{name}` → `{token:{id,name,createdUtc},secret}` |
| DELETE | `/api/proxies/{proxyId}/tokens/{tokenId}` | Revoke; 204, or 404 for an unknown token |

These routes require `proxies:write`. Creation returns `Cache-Control: no-store` and `Pragma: no-cache`. Use the standard one-time token screen, copy action and Done navigation. Keep `secret` only in the mounted creation result; do not store it in local/session storage, configuration drafts, query data, analytics or logs. After leaving/reloading, show metadata and revoke actions only. Create a replacement token if its value was lost. Up to 64 active tokens are supported; names must contain 1–64 characters and be unique per proxy, ignoring case.

Normal proxy list/draft/read responses never expose token values or encrypted storage. The privileged standalone-service export contains runtime credentials for deployment and must be treated as a private server configuration; do not use that export as a token-view operation.

## Authorization header

Every newly created manual token is delivered as:

```http
Authorization: Bearer <secret>
```

This is the same header and scheme commonly used for JWT. The value is an opaque proxy credential, not a JWT to decode or rewrite. Do not put it in the URL, query, path, cookies, Basic authentication or WebSocket subprotocol.

* WS: set the header on the upgrade request and on proxy probes. The existing `WsCarrier.Handshake` already supports this. Leave legacy `WsEndpoint.Credentials` empty: that field selects Basic authentication and takes precedence over the bearer argument. The wstunnel CLI supports `-H 'Authorization: Bearer <secret>'`; its subprotocol JWT continues to describe the UDP target and does not contain the access token.
* HTTP GET/POST v2: send the header on challenge, probe, upload and download/poll requests. Continue using the same credential for the existing v2 signing protocol. The bearer header supplements the signature, epoch, time, nonce and replay checks; it does not replace them. Missing bearer headers are rejected for manual-token identities. Existing derived hello credentials retain their signed headerless compatibility path. A supplied header must use Bearer and match the signed credential identity.
* QUIC/WebTransport: send the same header on connection establishment. The listener enforces authorization and destination restrictions together.

Both hello contracts add `auth.header: "Authorization"`. Existing `auth.scheme` values are preserved: for example, `hmac-sha256-v2` describes HTTP signing, while the HTTP header still uses `Bearer`. Forward `Authorization` through any reverse proxy/CDN to the origin. Do not log it or enable shared caching of authenticated transport responses. TLS remains mandatory for every proxy mode.

## Anonymous access

The management request/response field is `allowAnonymous`; new and migrated proxies default to `false`. An old PUT omitting it preserves the saved value. Show a checkbox using the standard setting controls. Both hello catalogs add `anonymous: boolean`.

When enabled, no private access token is required. WS and QUIC accept an omitted Authorization header. HTTP v2 still requires protocol framing, signing and replay checks; anonymous mode uses the public protocol credential `f4514728-b141-4b09-a1fe-4a5a5f83cf51`, also supplied as `auth.credential` in hello. It is a public protocol identifier, not a user secret. The client can omit Authorization in this mode. Never select anonymous mode as a fallback for an invalid/revoked private token.

Destination scope and TLS checks apply equally to anonymous requests. Revoking a token does not exclude its former holder while anonymous access remains enabled. Turning anonymous access off removes the public protocol credential and updates active runtime generations.

## Server application and deployment

Token creation/revocation and access-mode changes refresh the existing WS, HTTP GET/POST and QUIC runtime. Manual tokens are encrypted in the database; active native configurations contain the material required by their wire protocol. Token revocation and disabling anonymous access retire existing WS generation leases; adding a token does not retire them. HTTP and QUIC rebuild their credential restrictions. The application boundary is the completed runtime apply, not merely a database write.

Standalone export includes `allowAnonymous` and `bearerIds`. The latter contains HTTP credential identities that require the Bearer header. Preserve these fields when installing an exported catalog. Older catalogs without `bearerIds` retain their prior signed HTTP behavior. Local scope is exported as a concrete destination snapshot; export again after relevant interface or token changes.
