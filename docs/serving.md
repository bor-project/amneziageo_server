# Where the panel answers

The panel holds where it answers itself, on the **Server** and **Certificates** tabs of **Settings**. What
it holds is taken at start: `Save`
writes a change down, and it takes hold after `Restart`, the button that shows in the header while a saved
change waits for it. `Cancel` drops what is not saved yet, and an edit left unsaved stays while other pages
are opened. The language takes hold at once.

| Setting | What it does |
|---|---|
| Listen addresses | the addresses of the host the panel binds, empty for every address it carries |
| Listen domains | the names the panel answers to, empty for any; a request carrying another name answers 404, a caller from the loopback is let through |
| Port | the port the panel binds, 8443 when nothing is set |
| Path | what follows the port, `/` for the root: `/panel/` puts the panel there and everything outside it answers 404 |
| Certificate domain | a directory of `/etc/letsencrypt/live`, picking one fills the two paths below it |
| Certificate path | the chain in PEM, empty for the certificate the configuration names |
| Certificate key path | the key of that chain, taken together with it |
| Language | the language the panel opens in, until the browser is told otherwise |

`POST /api/panel/restart` stops the server, systemd starts it again. `GET /api/panel` carries `pending`,
true while the saved settings differ from the ones the panel started under; the language does not count.

The page of the panel carries the path in its `base` tag, so the interface and `/api` follow the panel
wherever it sits.

## The first start

Until the panel holds settings of its own, the `Web:Listen` list of `appsettings.json` names what it binds.
Each entry is an address or an interface of the host with a port:

| Entry | Binds |
|---|---|
| `*:8443` | every address of the host |
| `127.0.0.1:8443` | that address alone |
| `[::1]:8443` | an address of the sixth version, in brackets |
| `lo:8443` | every address the `lo` interface carries |
| `awg1:8443` | every address the tunnel carries, so the panel answers inside it and nowhere else |

```json
{
  "Web": {
    "Listen": [ "lo:8443", "awg1:8443" ]
  }
}
```

Addresses of an interface are read once, at start. An interface brought up later carries no listener until
the server is restarted, and one the host does not carry at all is written down in the log and skipped. When
nothing in the list resolves, the server refuses to start and says so.

Link-local addresses are left out.

The same list is moved by environment variables, one per entry:

```
Web__Listen__0=lo:8443
Web__Listen__1=awg1:8443
```

The list becomes the settings the panel starts holding: the port of its first entry and every address behind
the entries carrying that port, `*` for every address of the host. From then on the panel rules, and the list
is read again only when the settings are dropped from the database.

## Under a certificate

`Web:Certificate` names the certificate chain in PEM and `Web:CertificateKey` the private key behind it.
With both named every entry of the listen list answers over TLS, with neither the panel answers plainly.
Naming a pair of paths in the panel does the same, and picking a certificate domain there fills them with
`fullchain.pem` and `privkey.pem` of that directory.

```json
{
  "Web": {
    "Listen": [ "*:8443" ],
    "Certificate": "/etc/letsencrypt/live/example.org/fullchain.pem",
    "CertificateKey": "/etc/letsencrypt/live/example.org/privkey.pem"
  }
}
```

`Web:CertificateRoot` moves the directory the certificate domains are looked for in, `/etc/letsencrypt/live`
by default.

The chain is read again whenever the file behind it changes, so a renewed certificate is taken without a
restart. A certificate the settings name and the host does not carry stops the server at start.

Saving a pair in the panel reads both files and refuses one that does not load, with a code that names the
file: `certificate-` for the chain, `certificate-key-` for the key.

| Code | Means |
|---|---|
| `certificate-no-folder`, `certificate-key-no-folder` | the folder of the file does not exist |
| `certificate-not-found`, `certificate-key-not-found` | the file is not there |
| `certificate-is-folder`, `certificate-key-is-folder` | the path names a folder, not a file |
| `certificate-denied`, `certificate-key-denied` | the panel has no access to the file |
| `certificate-unreadable`, `certificate-key-unreadable` | the file does not read |
| `certificate-empty`, `certificate-key-empty` | the file is empty |
| `certificate-invalid` | the file holds no valid certificate |
| `certificate-key-invalid` | the file holds no valid private key |
| `certificate-key-encrypted` | the private key is locked by a password |
| `certificate-key-mismatch` | the private key belongs to another certificate |

## Running it

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Api
```

The web interface is built into `wwwroot` of the API and served from the same port, so the panel is at
`http://<address>:8443/`.

While the interface is being worked on, run Vite next to the server and open it instead: it reloads on every
change and sends `/api` on to the server.

```
npm --prefix amneziageo-web run dev
```

| Address | What answers |
|---|---|
| `http://<host>:8443/` | the server with the interface built into it |
| `http://<host>:5173/` | Vite, sending `/api` on to port 8443 |

## Paths

| Variable | What it moves |
|---|---|
| `AMNEZIAGEO_DB` | the database file, `/var/lib/amneziageo-server/server.db` by default |
| `AMNEZIAGEO_SIGNING_KEY` | the signing key, `/etc/amneziageo-server/signing.pem` by default |
