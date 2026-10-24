# Terms of use

The terms of use, the privacy notes and the third-party components of the server lie in the root of the repository:
[TERMS.md](../TERMS.md), [PRIVACY.md](../PRIVACY.md) and [THIRD-PARTY.md](../THIRD-PARTY.md), each with a Russian
twin (`.ru.md`). The panel shows these very files, and the site of the documentation copies them. The terms and the
privacy notes carry a version on their second line; `TermsEndpoints.Version` names the one the panel asks to accept,
and a test holds the two together.

## The installation

The menu asks for the consent before anything else, with the short terms and the address of the full text. The
answer `n` stops the installation before it changes anything. In a file of answers the key is `license`: `yes`
accepts; without it and without a terminal the installation stops.

## The panel

Until the version the panel asks for is accepted, every page of a signed-in account gives way to the terms:

- an account with `access:write` reads the terms of use and the privacy notes and accepts them, or signs out;
- any other account learns that an administrator has not accepted them yet, can read them and sign out.

A container asks for the consent this way alone. The consent belongs to the panel: one acceptance lets every
account in. A new version of the texts is asked for once again.

The page About, at the bottom of the menu, shows the version of the panel, the license, the latest acceptance,
what the server of updates gets, and the texts.

## The routes

| Route | Rights | What it does |
|---|---|---|
| `GET /api/terms` | any signed-in caller | `{ version, accepted, last }`: the version asked for, whether it is accepted, the latest acceptance `{ version, login, acceptedUtc }` or `null` |
| `POST /api/terms` | `access:write` | accepts `{ "version": N }`; a version other than the one asked for answers 409 `terms-version`; the acceptance goes to the journal of actions as `terms.accept` |

The database keeps every acceptance in the table `Terms`: the version, the account and the instant.
