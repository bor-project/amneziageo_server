# Where the panel answers

The `Web:Listen` list of `appsettings.json` names what the panel binds. Each entry is an address or an
interface of the host with a port:

| Entry | Binds |
|---|---|
| `*:5080` | every address of the host |
| `127.0.0.1:5080` | that address alone |
| `[::1]:5080` | an address of the sixth version, in brackets |
| `lo:5080` | every address the `lo` interface carries |
| `awg1:5080` | every address the tunnel carries, so the panel answers inside it and nowhere else |

```json
{
  "Web": {
    "Listen": [ "lo:5080", "awg1:5080" ]
  }
}
```

Addresses of an interface are read once, at start. An interface brought up later carries no listener until
the server is restarted, and one the host does not carry at all is written down in the log and skipped. When
nothing in the list resolves, the server refuses to start and says so.

Link-local addresses are left out.

The same list is moved by environment variables, one per entry:

```
Web__Listen__0=lo:5080
Web__Listen__1=awg1:5080
```

## Running it

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Api
```

The web interface is built into `wwwroot` of the API and served from the same port, so the panel is at
`http://<address>:5080/`.

While the interface is being worked on, run Vite next to the server and open it instead: it reloads on every
change and sends `/api` on to the server.

```
npm --prefix amneziageo-web run dev
```

| Address | What answers |
|---|---|
| `http://<host>:5080/` | the server with the interface built into it |
| `http://<host>:5173/` | Vite, sending `/api` on to port 5080 |

## Paths

| Variable | What it moves |
|---|---|
| `AMNEZIAGEO_DB` | the database file, `/var/lib/amneziageo-server/server.db` by default |
| `AMNEZIAGEO_SIGNING_KEY` | the signing key, `/etc/amneziageo-server/signing.pem` by default |
