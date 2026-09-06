# Accounts and login

Two ways in: a panel account with a password, and a host account of the server itself. Both end in the same
pair of tokens, and past that point the code no longer knows which one was used.

Accounts, roles and claims are kept by ASP.NET Core Identity over EF Core on SQLite: `AspNetUsers`,
`AspNetRoles`, `AspNetUserRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, beside the tables the server adds of
its own (`Sessions`, `RefreshTokens`, `AuditEntries`). The schema is carried by EF migrations in
`AmneziaGeo.Server.Dal/Migrations` and applied on start.

## Roles and rights

A right is a claim of type `scope`. A role carries a set of them, an account holds a role, and what the
account may do is the claims of its role plus the claims given to the account itself.

| Right | What it opens |
|---|---|
| `state:read` | reading the state |
| `clients:write` | managing clients |
| `interfaces:write` | managing interfaces |
| `access:write` | managing accounts and roles |

The server carries one role of its own, `admin`, and it is the one every right belongs to. It is neither
removed nor stripped: on every start the claims of `admin` are brought level with the rights the build knows,
so a new right reaches the administrator without a migration. Every other role is made in the panel or in the
console.

**A new feature means a new right in `Scopes`, that right in the roles it belongs to, `RequireScope` on the
route and the menu item behind the same right.**

## The first administrator

`init` works only while the panel carries no enabled administrator, the way Portainer and Keycloak keep their
setup window open once. It registers the host account the utility runs as, or, with `--user`, makes a panel
account with a password. Either way the account is given `admin`.

```
amneziageo-server-cli init
amneziageo-server-cli init --user admin --generate
```

## Host accounts

A host account signs in with no password: the process runs as that user, and the server reads its name and
groups. Membership in a group is the explicit act an administrator of the host performs, and it is what maps
the account to a role. `Auth:HostGroups` holds the map, `amneziageo-admin` to `admin` by default, and the
first group of the list an account belongs to wins.

`root` counts as an administrator while `RootIsAdmin` holds.

`HostLogin` decides what happens to a host user no group maps:

- `strict` (default) refuses the login, the way Grafana refuses one with `role_attribute_strict`;
- `register` registers the account with no rights and lets it in;
- `off` closes host login altogether.

A group sets the role on every login. Names the host carries are left to the host: `user add` refuses one that
`/etc/passwd` already holds, so a panel account can never collide with a host account of the same name.

A host account signs in over HTTP only by a password of the panel, since a request cannot carry the account a
browser runs as. An administrator sets one with `user passwd <name>`; the account keeps its role and its host
login as they were.

## Passwords

Hashed by the Identity hasher: PBKDF2-HMAC-SHA512, 210 000 iterations, a salt per password. Ten wrong answers
lock the account for fifteen minutes, counted by the clock the server runs on. A password an administrator
sets has to be replaced on first use: that login returns an access token holding `password:change` and
nothing else, and no refresh token at all.

## Tokens

The access token is a compact JWS signed with ES256 by the key at `SigningKeyPath`, written on first run with
owner-only permissions. It lives ten minutes and carries the account, the session and the rights.

The refresh token is a random secret, stored as a SHA-256 hash. Every exchange rotates it. A spent token
presented again inside the twenty second grace window is taken as a parallel request of the same browser and
answered with a fresh one; past that window it is taken as theft and the whole session ends. A session is
extended to at most thirty days, a refresh token to at most fourteen.

## The routes

| Route | Takes | Gives back |
|---|---|---|
| `POST /api/auth/login` | `{ user, password }` | the tokens and the account |
| `POST /api/auth/refresh` | `{ refresh }` | a fresh pair |
| `POST /api/auth/logout` | a bearer token | nothing, the session is closed |
| `GET /api/auth/me` | a bearer token | the account behind it |
| `POST /api/auth/password` | `{ current, next }` | a fresh pair, the old session closed |

A refusal comes back as `{ error, message }`: `wrong-credentials`, `locked`, `disabled`, `no-password`,
`weak`, `session-ended`, `refresh-expired`, `refresh-replayed`, `refresh-unknown`, `forbidden`,
`unauthorized`.

An access token that has to be replaced carries `password:change` and nothing else, and comes with no refresh
token, so the interface can only take its holder to the password screen.

Signing in as the host account itself works only in the console utility, where the server reads the account
the process runs as. Over HTTP such an account signs in by the password of the panel it was given.

## Configuration

| Variable | What it moves |
|---|---|
| `AMNEZIAGEO_DB` | the database file, `/var/lib/amneziageo-server/server.db` by default |
| `AMNEZIAGEO_SIGNING_KEY` | the signing key, `/etc/amneziageo-server/signing.pem` by default |
| `AMNEZIAGEO_MIN_PASSWORD` | the shortest password an account may carry, eight by default |

The server reads the same floor from `Auth:MinimumPasswordLength`, along with the rest of the login settings:
`Auth:AccessLifetime`, `Auth:RefreshLifetime`, `Auth:HostLogin`, `Auth:HostGroups`, `Auth:FailedAttempts`.

## Commands

```
amneziageo-server-cli                                 open the menu
amneziageo-server-cli init [--user <login>]           make the first administrator
amneziageo-server-cli login [--user <login>] [--json] sign in and print the tokens
amneziageo-server-cli refresh <token>                 trade a refresh token for a pair
amneziageo-server-cli whoami                          what the panel makes of this host account
amneziageo-server-cli user list
amneziageo-server-cli user add <login> [--role <role>] [--generate] [--permanent]
amneziageo-server-cli user passwd <login> [--generate] [--permanent]
amneziageo-server-cli user role <login> <role>
amneziageo-server-cli user enable | disable | remove <login>
amneziageo-server-cli role list
amneziageo-server-cli role add <name> [--title <title>] <right>...
amneziageo-server-cli role set <name> [--title <title>] [<right>...]
amneziageo-server-cli role remove <name> [--yes]
```

The last enabled account that holds `access:write` cannot be demoted, disabled or removed.

## Managing accounts over HTTP

The panel does everything the console does, on the `Users` tab of `Panel settings` -> `Access`, and every
route below asks for `access:write`. What an account may do follows from its role alone; claims given to an
account one by one are left to the console and the panel neither shows nor sets them. The account of the
caller changes its own password on `Panel settings` -> `General`.

| Route | What it does |
|---|---|
| `GET /api/users` | every account with its role, state and rights |
| `POST /api/users` | adds an account of the panel with its first password |
| `PATCH /api/users/{name}` | sets the role or the state |
| `PUT /api/users/{name}/password` | replaces the password of an account |
| `DELETE /api/users/{name}` | removes an account with its sessions and tokens |

`AccountManager` holds the rules, so the console and the panel refuse the same things: a name of the wrong
shape (`bad-name`), a name the panel already carries (`name-taken`), a name the host carries (`host-name`),
a password below the floor (`weak`), a role that is not there (`unknown-role`), the last enabled
administrator (`last-admin`), and the account of the caller itself (`self`). An account changes its own
password through `POST /api/auth/password`, which needs only `password:change`.

## Managing roles over HTTP

Roles live on the `Roles` tab of the same page, and these routes ask for `access:write` as well.

| Route | What it does |
|---|---|
| `GET /api/roles` | every role with its rights and the number of accounts, and the rights the server knows |
| `POST /api/roles` | adds a role carrying the rights it is given |
| `PATCH /api/roles/{name}` | replaces the name shown, the rights, or both |
| `DELETE /api/roles/{name}` | removes a role no account holds |

`RoleCatalog` holds the rules: a name of the wrong shape (`bad-name`), a name already taken (`name-taken`),
a right the server does not know (`unknown-scope`), the built in role (`builtin-role`), and a role that is
still given to accounts (`role-in-use`).
