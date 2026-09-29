# Updates from the panel

The panel looks for its releases on GitHub and puts a newer one on by itself, whether it runs from the package
or in a container.

## Where the releases come from

A release is a tag `vX.Y.Z.W` on a commit of the branch `master` of `bor-project/amneziageo_server`; a tag outside
`master` builds nothing. The workflow `.github/workflows/release.yml` builds it: the packages for x64 and arm64,
beside each package its files packed one by one (`.pack`) and their list (`.files`), the image
`ghcr.io/bor-project/amneziageo-server` for both, and `update.json`, which names the version, the image pinned to
its digest, the digest of every package and of its list.
`update.json.sig` signs the manifest with the key of the releases. A tag whose fourth number is above zero, or
that carries a suffix, makes a prerelease.

A minute after it starts and every 12 hours after that, the panel reads the list of releases, takes the newest one
of its channel that carries `update.json` and `update.json.sig`, checks the signature against the key it carries,
and offers the release when it is newer than itself. A manifest that does not check out is not offered, and the
panel names why.

| Setting | Default | What it does |
|---|---|---|
| `Update__Channel` | `stable` | `test` takes the prereleases as well |
| `Update__CheckHours` | `12` | how often the releases are looked over; `0` leaves it to the panel |
| `Update__Repository` | `bor-project/amneziageo_server` | whose releases are read |
| `Update__Manifest` | | the address of an `update.json` read in place of the releases, its signature beside it with `.sig` |
| `Update__Key` | | a file with the public key in PEM, in place of the one the panel carries |
| `Update__Directory` | `update` beside the database | the downloads, the logs and the record of the last update |
| `Update__Docker` | `/var/run/docker.sock` | the socket of the Docker daemon |

The settings go into `server.env` like any other.

**Receive prerelease versions** on the **Server** tab of **Settings** takes the prereleases as well, without a
restart: the panel looks the releases over again once the tab is saved. `Update__Channel=test` takes them whatever
the tab says. `GET /api/update` names the channel in `channel`.

## The routes

| Route | Right | What it does |
|---|---|---|
| `GET /api/update` | `state:read` | the version that runs, the release newer than it, whether the panel can put it on by itself, how the last update went |
| `POST /api/update/check` | `updates:write` | looks the releases over now |
| `POST /api/update/apply` | `updates:write` | `{"version": "<version>"}` moves the panel to the newest release, that one or a newer one; a look older than ten minutes is taken again first, waiting for it at most ten seconds |

`blocker` in the answer tells why the panel cannot put a release on by itself:

| `blocker` | Why |
|---|---|
| `manual` | the panel runs neither from the package nor in a container |
| `no-root` | the package runs under a user other than root |
| `no-systemd` | the host carries no `systemd-run` |
| `no-docker` | the container does not reach the socket of the daemon |
| `no-container` | the panel does not find its own container |
| `no-compose` | the container was not started by compose |
| `compose-elsewhere` | a compose file lies outside the directory of the project |
| `no-data` | the data directory is not a mount of the container |

## From the package

The panel puts the package for the architecture of the host together in `update/stage-<version>`. It reads the
list of the files of the package, checks it against the digest in `update.json` and looks for every file among the
files of the release it runs and of its web interface. The files it holds are copied, the others are fetched from
the pack of the release in parts (HTTP `Range`), a few requests in all, and every file is checked against its
digest in the list. Between two releases these are the assemblies of the panel, and the web interface and the
websocket tool where they changed: a megabyte or a few out of about 55.

The panel downloads the whole package instead, checks its digest and unpacks it where the release carries no
list, the files of the release are not served in parts, a file does not match its digest, or more than three
quarters of the package changed; the log of the update says which. A panel before these lists always downloads
the whole package, so the first update onto a release that carries them is whole too.

Each line of the list names one file: its digest, its rights in octal, its size, where it starts in the pack and
how many bytes it takes there, and its path in the package. Each file lies in the pack compressed by gzip on its
own, one after another in the order of the list; `deploy/publish.sh` writes both beside the package.

Then the panel starts `install.sh` of the package apart from itself, through `systemd-run` as the unit
`amneziageo-server-update-<version>`. The script does what it does by hand, see [install.md](install.md): it
stops the server, copies the database aside, starts the new release and goes back to the one before where the
new one does not come up. What it prints goes to `update/<version>.log`, how it ended to `update/<version>.rc`.

## In a container

The container reaches the daemon through its socket, which `compose.yaml` hands it:

```
      - /var/run/docker.sock:/var/run/docker.sock
```

The socket gives the container the host: whoever holds the panel holds the host as well. A host that does not
want that leaves the line out and updates by hand, see [docker.md](docker.md).

The layers of the image go from those that change least to those every release changes: the packages of the
system, the websocket tool, the scripts of the container, the libraries the panel takes, the assemblies of the
panel, its web interface and the menu. The workflow builds the image on the cache of the release before, kept in
the registry as `buildcache-amd64` and `buildcache-arm64`, so a layer whose sources did not change stays the same
layer, and the daemon pulls only the top ones, a megabyte or two. A new base image or new packages of the system
change every layer above them once.

The panel pulls the image of the release by its digest, names it the way the project names the image of the
panel, with the version as the tag, and starts a container of the new image, `amneziageo-server-update-<version>`,
that moves the panel. It copies the database into `backup` of the data directory, writes `AMNEZIAGEO_TAG=<version>`
into `.env` of the project, stops the panel and makes its container over onto the new image through the daemon:
the new one is created with the settings of the one that ran, and the image is all that changes. The old container
waits under its name with the version it ran until the new one comes up healthy. Where that does not happen within
three minutes, the new container goes away, the database and `.env` go back and the old one runs again.

The files of compose are not read, so the update goes through wherever they lie and whatever they name; `.env`
takes the tag so that `docker compose up -d` by hand later starts the same image. The containers of the updates
that ended are removed at the next look.

## What the panel shows

The head of `Overview` shows the version of the panel and, beside it, the release newer than it with a link to
its page. An account that holds `updates:write` moves the panel to it with `Update`; the page follows the update
and loads the new panel once it answers. An update that failed stays in the head, with its log, until the next
one.

## The key of the releases

The key is an EC key on the curve P-256, made once:

```
openssl ecparam -name prime256v1 -genkey -noout -out update-signing.pem
openssl ec -in update-signing.pem -pubout -out update-key.pem
```

`update-signing.pem` goes into the secret `UPDATE_SIGNING_KEY` of the repository and nowhere else,
`update-key.pem` into `UpdateSignature.Published`. A panel built without the key names `update-no-key` and
offers no release.
