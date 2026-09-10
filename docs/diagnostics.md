# Diagnostics

The **Diagnostics** tab of **Settings** shows what the panel runs on, the kernel rules the routing rules
build, and the latest records of the log of the panel.

| Route | Right | Returns |
|---|---|---|
| `GET /api/diagnostics` | `access:write` | the runtime, the system, the kernel release and what `wstunnel --version` prints |
| `GET /api/diagnostics/log` | `access:write` | the latest 500 records of the log, the newest first |
| `GET /api/rules/ruleset` | `routing:write` | the kernel rules the routing rules build |

The versions of the panel and of the AmneziaWG module come from `/api/health` and `/api/overview`.

The journal keeps what passes the `Logging` levels of `appsettings.json`: `Information` by default,
`Warning` for ASP.NET Core and Entity Framework. It lives in memory and starts empty after a restart;
journald keeps the whole log of the service.
