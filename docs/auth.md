# Accounts and login

Two ways in: a panel account with a password, and a host account of the server itself. Both end in the same
pair of tokens, and past that point the code no longer knows which one was used.

Scripts and other services come in by a long lived token that acts by a role, see [API tokens](#api-tokens),
and the routes they call are gathered in [The HTTP API](api.md).

Accounts, roles and claims are kept by ASP.NET Core Identity over EF Core on SQLite: `AspNetUsers`,
`AspNetRoles`, `AspNetUserRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, beside the tables the server adds of
its own (`Sessions`, `RefreshTokens`, `ApiTokens`, `AuditEntries`). The schema is carried by EF migrations in
`AmneziaGeo.Server.Dal/Migrations` and applied on start.

## Roles and rights

A right is a claim of type `scope`. A role carries a set of them, an account holds a role, and what the
account may do is the claims of its role plus the claims given to the account itself.

| Right | What it opens |
|---|---|
| `state:read` | reading the state |
| `clients:write` | managing clients |
| `interfaces:write` | managing interfaces |
| `routing:write` | managing routing |
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

## Privileged accounts

An account of the panel is a user of the host as well when it is added with `User of the system`. The panel
gives the host what it needs and keeps the two together:

| Step | What the host takes |
|---|---|
| Groups | `amneziageo`, which reaches the files of the panel, and `amneziageo-<role>`, which carries the role |
| The user | `useradd` with a home and `/bin/bash` where the host carries no such user, `usermod --append` where it does |
| The key | the public key of the account written to `~/.ssh/authorized_keys`, owned by the user, `600` under a `700` directory |
| The files | the database and the signing key handed to `amneziageo` at `0660`, their directory at `2770` |

No password of the host is set: the user signs in to the host by its key alone. The panel keeps the public key
beside the account and writes it again whenever it is replaced; `bad-host-key` refuses a line the host does not
take.

Switching the account off closes the way in (`usermod --lock --expiredate 1`) and takes the user out of both
groups, switching it on opens them again. Removing the account takes the user out of the groups and leaves the
user of the host where it is, with its home and its files.

Changing the role moves the user between the groups of the roles. The role of the panel is what the account
holds, so a host login of such a user carries its role even where `Auth:HostGroups` names no group.

The service runs under `UMask=0002`, so the files SQLite makes beside the database stay open to the group. A
member of `amneziageo` runs `AmneziaGeo.Server.Cli` without `sudo`; putting interfaces on the host still asks
for root.

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

## API tokens

A script or another service signs in by a long lived token instead of a password. A token is minted for a role
and acts with the rights the role carries at the moment of the call, so a change of the rights of the role
reaches its tokens at once. A token belongs to no account: switching accounts off or removing them leaves it
alone, and only revoking it or the end of its lifetime stops it. It holds no `password:change` and has no
session, so `POST /api/auth/password` refuses it, `POST /api/auth/logout` does nothing, and `GET /api/auth/me`
answers with the name and the role of the token. A role that tokens act by stays until they are revoked.

The token is `agt_` and 32 random bytes in base64url. It travels in the same header as an access token,
`Authorization: Bearer agt_...`, and the server tells the two apart by the prefix. Only its SHA-256 hash is
kept, so the secret is shown once, when it is minted. A token lives the number of days it is minted with, from
1 to 3650, or has no end. The panel notes when and from which address a token was last used, at most once a
minute or on a new address.

Tokens live on the `Tokens` table of `Settings -> Users and roles`, and these routes ask for `access:write`.

| Route | What it does |
|---|---|
| `GET /api/tokens` | every token with its role, lifetime and last use |
| `POST /api/tokens` | `{ name, role, days }` mints a token and hands its secret back once |
| `DELETE /api/tokens/{id}` | revokes a token |

`ApiTokenManager` holds the rules, so the console and the panel refuse the same things: a name outside 1 to 64
characters (`bad-token-name`), a name another token carries (`token-name-taken`), a lifetime outside 1 to 3650
days (`bad-token-lifetime`), a role that is not there (`unknown-role`), a token that is not there
(`unknown-token`). Minting and revoking go to the audit trail as `token.mint` and `token.revoke`, without the
secret.

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
amneziageo-server-cli token list
amneziageo-server-cli token add <name> --role <role> [--days <n>]
amneziageo-server-cli token revoke <id> [--yes]
```

The last enabled account that holds `access:write` cannot be demoted, disabled or removed.

## Managing accounts over HTTP

The panel does everything the console does, on the `Users` tab of `Access`, and every
route below asks for `access:write`. What an account may do follows from its role alone; claims given to an
account one by one are left to the console and the panel neither shows nor sets them. The account of the
caller changes its own password from the account menu in the header.

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
| `DELETE /api/roles/{name}` | removes a role no account and no token holds |

`RoleCatalog` holds the rules: a name of the wrong shape (`bad-name`), a name already taken (`name-taken`),
a right the server does not know (`unknown-scope`), the built in role (`builtin-role`), and a role that is
still given to accounts (`role-in-use`) or carried by tokens (`role-has-tokens`).
