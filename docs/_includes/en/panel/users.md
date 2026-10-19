# {{ page.title }}

The "Users and roles" tab carries three tables: "Users", "Roles" and "Tokens". People sign in to the panel with an account of the panel and a password; scripts with a long-lived token that acts on behalf of a role.

{% include shot.html name="users" alt="Users and roles: the users admin and root, the built-in Administrator role, a token" caption="An administrator, the system user and a token for scripts." %}

## Users

| Column | What it shows |
|---|---|
| Name | the name for signing in |
| Display name | how the account is signed |
| Account type | "Panel", or "System": an account that also stands behind a user of the host |
| Role | the role or "no role" |
| Status | "enabled" or "disabled" |

"Add" opens a new account: the name (lower case letters, digits, dot, dash and underscore, starting with a letter), the display name, the role, the password with the "Generate" button, "Require a password change" (the new user changes the issued password at the first sign-in){% if page.kind == "service" %}, "User of the system" with the "Public ssh key" field, see below{% endif %}. The page of an account changes the role and the state, "Password" in the row menu sets a new password, "Remove" removes the account with its sessions. Everyone changes their own password in the account menu in the header. A password shorter than eight characters is not accepted (`AMNEZIAGEO_MIN_PASSWORD` in the environment sets another threshold); ten misses lock the account for fifteen minutes. The last enabled account with the "Manage access" permission cannot be demoted, disabled or removed, and none of this can be done to your own account.

A name a user of the host carries is not given to an account of the panel: "The host carries a user with this name, that name is left to it".
{% if page.kind == "service" %}
### System users

An account with "User of the system" checked also stands behind a user of the host: the panel makes the user (`useradd` with a home directory and `/bin/bash`, or completes an existing one), puts it into the groups `amneziageo`, which reaches the files of the panel, and `amneziageo-<role>`, which carries the role, and puts the public key from the field into its `~/.ssh/authorized_keys`; it sets no host password, and the user signs in to the host by key. The files of the panel are given to the `amneziageo` group with `0660` permissions, so a member of the group runs `AmneziaGeo.Server.Cli` without `sudo`; raising interfaces still needs root. Disabling closes the sign-in to the host and takes the user out of the groups, enabling brings it back; removing takes the user out of the groups and leaves the user of the host with its home. Changing the role moves it between the role groups.

A user of the host signs in to the panel only with the password of the panel: a request from a browser does not carry the account it runs under; in the console `whoami` shows what the panel thinks of the account of the host the utility runs as. The `amneziageo-admin` group gives the `admin` role at a sign-in from the host (`Auth__HostGroups`), and root counts as an administrator. In the console menu, item 7 offers to make a user of the host an administrator of the panel, and so does the installation when the login of the first administrator is a user of the host.
{% else %}
In the container the accounts of the host are off (`Auth__HostLogin=Off`, `Auth__HostUsers=false`): the panel signs in only with its own accounts and makes no system users, and the form has no "User of the system" box.
{% endif %}
## Roles

A permission is what a role opens; a role carries a set of permissions, and an account carries a role.

| Permission | What it opens |
|---|---|
| Read the state | reading everything: "Overview", the lists, the route test |
| Manage clients | clients, templates, routing presets |
| Manage interfaces | interfaces and their ports |
| Manage routing | rules, basic lists, channels, geo, DNS |
| Manage access | "Settings": server, certificates, subscriptions, users, roles, tokens, diagnostics, restoring from a backup |
| Update the panel | "Update" on "Overview" |
| Download backups | "Download a copy" on "Overview" |

The "Roles" table shows the code, the name, the rights and the number of accounts. The built-in `admin` role carries all the permissions and is neither removed nor cut down: at every start its permissions are lined up with those of the release, so a new permission reaches the administrator by itself. Roles of your own are made with "Add": a code (like the name of an account), a name and the checked permissions. A role given to accounts ("The role is given to accounts") or carrying tokens ("Tokens act by this role") is not removed.

## Tokens

A token for scripts and other services is issued under a role and acts with its permissions at the moment of the call, so a change to the permissions of the role reaches the tokens at once. A token does not belong to an account: disabling and removing accounts does not touch it, and it can be stopped only by revoking it or by its lifetime. It starts with `agt_`, goes in the same `Authorization: Bearer` header as a session token, and is kept only as a hash: the secret is shown once, when it is issued ("The token is shown once").

| Column | What it shows |
|---|---|
| Name | up to 64 characters, one of a kind in the panel |
| Role | the role on behalf of which the token acts |
| Created, Valid until | the lifetime: 1 to 3650 days or "no end" |
| Last used | when and from which address the token was last used, "never" |
| Status | "active" or "expired" |

"Add" asks for the name, the role and the lifetime in days, and "Revoke" in the row menu revokes it. From the console the same is done by `amneziageo-server token list`, `token add <name> --role <role> [--days <n>]` and `token revoke <id>`. How a token goes into the API: [HTTP API](../../api.html).

## Refusals

| Message | What stands behind it |
|---|---|
| The name takes lower case letters, digits, dot, dash and underscore, and starts with a letter; The name is already taken; The host carries a user with this name, that name is left to it | the name of an account or a role |
| The password is shorter than eight characters | the password |
| There is no such role; There is no such right; The server keeps this role as it is; The role is given to accounts; Tokens act by this role | roles |
| This is the last administrator; An account does not do this to itself | protection against losing access |
| The public key is not one the host takes | the ssh key of a system user |
| A token name takes 1 to 64 characters; A token with this name is already there; The lifetime is a whole number of days from 1 to 3650; There is no such token | tokens |
