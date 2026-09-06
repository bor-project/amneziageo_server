# Accounts and login

Two ways in: a panel account with a password, and a host account of the server itself. Both end in the same
pair of tokens, and past that point the code no longer knows which one was used.

## Roles

A role is what an account holds; rights are what a role carries.

| Role | Rights |
|---|---|
| `admin` | `state:read` `clients:write` `interfaces:write` `access:write` |
| `operator` | `state:read` `clients:write` |
| `viewer` | `state:read` |
| `none` | nothing |

Rights can be granted on top of a role one by one (`user grant`), and the account holds the union.

## The first administrator

`init` works only while the panel carries no enabled administrator, the way Portainer and Keycloak keep their
setup window open once. It registers the host account the utility runs as, or, with `--user`, makes a panel
account with a password.

```
amneziageo-server-cli init
amneziageo-server-cli init --user admin --generate
```

## Host accounts

A host account signs in with no password: the process runs as that user, and the server reads its name and
groups. Membership in a group is the explicit act an administrator of the host performs, and it is what maps
the account to a role:

| Group | Role |
|---|---|
| `amneziageo-admin` | `admin` |
| `amneziageo-operator` | `operator` |
| `amneziageo` | `viewer` |

`root` counts as an administrator while `RootIsAdmin` holds.

`HostLogin` decides what happens to a host user no group maps:

- `strict` (default) refuses the login, the way Grafana refuses one with `role_attribute_strict`;
- `register` registers the account with no rights and lets it in;
- `off` closes host login altogether.

A group only raises the role. Rights taken away in the panel stay away until the group says otherwise.

Names the host carries are left to the host: `user add` refuses one that `/etc/passwd` already holds, so a
panel account can never collide with a host account of the same name.

## Passwords

Derived with PBKDF2-HMAC-SHA512, 210 000 iterations, a 16 byte salt per password. Ten wrong answers lock the
account for fifteen minutes. A password an administrator sets has to be replaced on first use: that login
returns an access token holding `password:change` and nothing else, and no refresh token at all.

## Tokens

The access token is a compact JWS signed with ES256 by the key at `SigningKeyPath`, written on first run with
owner-only permissions. It lives ten minutes and carries the account, the session and the rights.

The refresh token is a random secret, stored as a SHA-256 hash. Every exchange rotates it. A spent token
presented again inside the twenty second grace window is taken as a parallel request of the same browser and
answered with a fresh one; past that window it is taken as theft and the whole session ends. A session is
extended to at most thirty days, a refresh token to at most fourteen.

## Configuration

| Variable | What it moves |
|---|---|
| `AMNEZIAGEO_DB` | the database file, `/var/lib/amneziageo-server/server.db` by default |
| `AMNEZIAGEO_SIGNING_KEY` | the signing key, `/etc/amneziageo-server/signing.pem` by default |

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
amneziageo-server-cli user grant <login> <right>...
amneziageo-server-cli user revoke <login> <right>...
amneziageo-server-cli user enable | disable | remove <login>
```

The last enabled administrator cannot be demoted, disabled or removed.
