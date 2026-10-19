---
title: HTTP API
lang: en
topic: api
permalink: /api.html
description: The routes of the panel under /api, tokens for scripts, the shape of answers and the OpenAPI description.
---

# HTTP API

The panel does everything through the HTTP API under `/api`, and a script calls the same routes. Every route except `POST /api/auth/login`, `POST /api/auth/refresh` and `GET /api/health` needs a token in the `Authorization: Bearer` header.

## Tokens

- **Session token.** `POST /api/auth/login` with the body `{ "user": "...", "password": "..." }` returns a pair: an access token for ten minutes and a refresh token, which `POST /api/auth/refresh` exchanges for a new pair. The panel itself runs on these tokens, with the permissions of the role of the account.
- **Token for scripts.** A long-lived token starts with `agt_`, acts on behalf of a role and is shown once: it is issued in the "Tokens" table of "Settings" > "Users and roles" or with `amneziageo-server token add <name> --role <role> [--days <n>]`. A change to the permissions of the role takes effect on its tokens at once; disabling and removing accounts does not touch tokens. It is revoked in the same place or with `token revoke`.

Both tokens go in the same header:

```
curl -H "Authorization: Bearer agt_..." https://panel.example.com:8443/api/clients
```

If the panel has a path of its own, the path goes before `/api`: `https://panel.example.com:8443/<path>/api/clients`.

## Answers

A refusal comes as `{ "error": "<code>", "message": "<words>" }` with a status that fits: 400 for a request that failed validation, 401 without a token or with an expired one (`unauthorized`), 403 for a permission the role does not have (`forbidden`), 404 for what does not exist, 409 for a conflict. An unknown route answers 404 with `unknown-route`. The refusal codes of each section are listed on the pages of the panel, together with the text the panel shows for them.

Every `POST /api/<section>/{id}/switch` takes `{ "on": true }` or `{ "on": false }`; a body without `on` answers 400 and `incomplete`.

## OpenAPI description

`GET /api/openapi.json` returns an OpenAPI 3.1 document with all the routes: path, method, parameters and the shape of the request body. The security requirement of a route names the permission it asks for. The document is requested with a token, like any route. It has no response shapes: the pages below describe them.

## Routes by section

| Section | Page of the site | Reference in the repository with the routes |
|---|---|---|
| accounts, roles, tokens | [Users and roles](../service/panel/users.md) | [auth.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/auth.md) |
| interfaces | [Interfaces](../service/panel/interfaces.md) | [configs.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/configs.md) |
| clients | [Clients](../service/panel/clients.md) | [clients.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/clients.md), [devices.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/devices.md) |
| templates and routing presets | [Templates](../service/panel/templates.md), [Routing presets](../service/panel/presets.md) | [templates.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/templates.md) |
| subscriptions | [Subscriptions](../service/panel/subscriptions.md) | [subscriptions.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/subscriptions.md) |
| channels and groups | [Channels](../service/panel/channels.md) | [outbounds.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/outbounds.md), [balancers.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/balancers.md) |
| rules, basic lists, test | [Rules](../service/panel/rules.md), [Basic](../service/panel/basic.md), [Test](../service/panel/test.md) | [rules.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/rules.md) |
| resolver | [DNS](../service/panel/dns.md) | [dns.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/dns.md) |
| geo lists | [Geo lists](../service/panel/geo.md) | [geo.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/geo.md) |
| interface services: hello, speed, WebSocket, signal | [Interfaces](../service/panel/interfaces.md#services-on-the-tcp-port) | [services.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/services.md) |
| where the panel answers, certificate | [Server](../service/panel/server.md), [Certificates](../service/panel/certificates.md) | [serving.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/serving.md) |
| ports in the firewall of the host | [Interfaces](../service/panel/interfaces.md#ports-in-the-firewall) | [firewall.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/firewall.md) |
| database backups | [Overview](../service/panel/overview.md#backup) | [install.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/install.md#moving-to-another-server) |
| diagnostics, connection log | [Diagnostics](../service/panel/diagnostics.md) | [diagnostics.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/diagnostics.md) |
| host overview and services | [Overview](../service/panel/overview.md) | [overview.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/overview.md) |
| panel updates | [Overview](../service/panel/overview.md#updates) | [updates.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/updates.md) |

The pages of the panel are the same for the service and Docker; the links lead to the service version.

## What stays in the console

- `init`: the first administrator; before it nobody signs in to the panel.
- `device` and `family`: the kernel as it is, for debugging.

Import exists in both: the console reads files of the host, the API takes their text (`POST /api/configs/import`, `POST /api/clients/import`).

## Example

A token for a day under the `admin` role, a new client on interface 1 and its file:

```
amneziageo-server token add nightly --role admin --days 1
```

The last line of the output is the token itself; it is printed once.

```
API=https://panel.example.com:8443/api
curl -s -H "Authorization: Bearer agt_..." -H "Content-Type: application/json" \
  -d '{ "name": "anna-phone", "configId": 1 }' "$API/clients"
curl -s -H "Authorization: Bearer agt_..." "$API/clients/7/config"
```

The body of `POST /api/clients` needs only a name and the number of the interface: the panel picks the keys and the address itself, and the client is on. The answer of `GET /api/clients/{id}/config` carries the text of the file, the `vpn://` link and the subscription address.
