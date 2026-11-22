# Panel certificate inspection

Open **Settings → Certificates** to inspect the TLS certificate of the running
panel. The page shows the subject, issuer, subject alternative names, validity
period, public key algorithm and size, signature algorithm, SHA-256 fingerprint
and the certificates supplied with the leaf.

The certificate details and PEM downloads require `access:write`, the same
administration permission used for panel settings. A viewer or operator without
that permission cannot request them. Downloads use the authenticated panel API.

- **Certificate (PEM)** exports the leaf.
- **Chain (PEM)** exports the leaf and the chain supplied in its certificate file.
- **Refresh** reads the current certificate details again.

Exports contain certificate blocks only. Private keys and other contents of a
combined PEM file are omitted. The chain is not completed from the host trust
store or downloaded from an issuer.

## API

| Request | Result |
| --- | --- |
| `GET /api/panel/certificate` | `enabled` and the `certificates` array of public metadata |
| `GET /api/panel/certificate/leaf.pem` | Leaf certificate as `panel-certificate.pem` |
| `GET /api/panel/certificate/chain.pem` | Supplied chain as `panel-chain.pem` |

These paths are relative to the configured panel prefix. Responses use
`Cache-Control: no-store`. An unauthenticated request receives 401; an account
without the administration permission receives 403.

The source is the certificate identity loaded by the panel listener at startup.
Editing the saved certificate path does not change the displayed identity until
the panel restarts. Replacing certificate files at the running paths reloads the
identity through the same holder used by TLS connections.

A panel without its own TLS certificate returns `enabled: false` and an empty
array. Its PEM downloads return 404. Unreadable or invalid certificate files
return 503 with `certificate-unreadable`; the response does not expose file paths
or file contents.

The API describes the panel's own certificate, not a certificate terminated by a
CDN, a reverse proxy, or another SNI identity on a shared port. Compare those
external connections separately when diagnosing a different certificate at the
public address. Viewing an expired, future-dated or self-signed certificate does
not grant it trust or change TLS validation.

Certificate issuance and renewal are separate actions; inspection and export do
not run certbot or change certificate files.
