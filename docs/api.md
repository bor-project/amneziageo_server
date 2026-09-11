# The HTTP API

The panel does everything through the HTTP API under `/api`, and a script calls the same routes. Every route
except `POST /api/auth/login`, `POST /api/auth/refresh` and `GET /api/health` asks for a bearer token.

## Tokens

- The access token of a session, which `POST /api/auth/login` hands back together with a refresh token. It
  lives ten minutes, and the refresh token trades for the next pair. The panel runs on these, with the rights
  of the role of the account.
- A long lived token for scripts and other services. It acts by a role of its own, starts with `agt_`, is
  minted on the `Tokens` table of `Settings -> Users and roles` or by `amneziageo-server-cli token add`, and is
  shown once.

Both travel in the same header:

```
curl -H "Authorization: Bearer agt_..." https://panel.example:8443/api/clients
```

The details are in [Accounts and login](auth.md#api-tokens).

## Answers

A refusal comes back as `{ error, message }` with the status that fits it: 400 for a request the rules refuse,
401 for a missing or dead token (`unauthorized`), 403 for a right the role does not carry (`forbidden`), 404 for
something that is not there, 409 for a clash. A route the server does not know answers 404 with
`unknown-route`.

## The description

`GET /api/openapi.json` returns an OpenAPI 3.1 document of every route: its path, method, parameters and the
shape of the request body. The security requirement of a route names the right it asks for. The document asks
for a token like any other route. The shapes of the answers are not in it; the pages below describe each area.

## The routes by area

| Area | Page |
|---|---|
| accounts, roles, tokens | [auth.md](auth.md) |
| interfaces | [configs.md](configs.md) |
| clients | [clients.md](clients.md) |
| devices of a client | [devices.md](devices.md) |
| templates | [templates.md](templates.md) |
| subscriptions | [subscriptions.md](subscriptions.md) |
| channels | [outbounds.md](outbounds.md) |
| balancers | [balancers.md](balancers.md) |
| rules | [rules.md](rules.md) |
| DNS | [dns.md](dns.md) |
| geo sources | [geo.md](geo.md) |
| proxies | [proxy.md](proxy.md) |
| where the panel listens | [serving.md](serving.md) |
| diagnostics | [diagnostics.md](diagnostics.md) |
| the overview | [overview.md](overview.md) |

## What stays in the console

- `init`: the first administrator, made before any account can sign in.
- `device` and `family`: the kernel as it is, for debugging.
- `relay`: the process the relay services run.

The imports are in both: the console reads the files of the host, the API takes their text
(`POST /api/configs/import`, `POST /api/clients/import`).
