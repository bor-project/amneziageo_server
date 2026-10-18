---
title: For developers
lang: en
topic: develop/build
permalink: /build.html
description: The layout of the repository, building the server, the panel and the submodules, the package and the image, releases, the documentation site.
---

# For developers

The repository is [bor-project/amneziageo_server](https://github.com/bor-project/amneziageo_server). The server is written in .NET, the panel in React; the kernel module and the WebSocket proxy are submodules.

## What is where

| Path | What |
|---|---|
| `amneziageo-server/AmneziaGeo.Server.Core` | domain types, interfaces, rules |
| `amneziageo-server/AmneziaGeo.Server.Dal` | EF Core over SQLite, account stores and migrations |
| `amneziageo-server/AmneziaGeo.Server.Awg` | driving the kernel through generic netlink |
| `amneziageo-server/AmneziaGeo.Server.Auth` | accounts, roles as claims, passwords and tokens |
| `amneziageo-server/AmneziaGeo.Server.Api` | the HTTP API and the built web interface |
| `amneziageo-server/AmneziaGeo.Server.Cli` | the console utility |
| `amneziageo-web` | the interface of the panel, React and Vite |
| `amneziageo-tests` | tests |
| `amneziawg` | the kernel module as a submodule |
| `wstunnel` | the WebSocket proxy as a submodule |
| `deploy` | the systemd service, the package installer, the console menu, the container |
| `docs` | the English reference by section and this site |
| `tools` | the generator of netlink constants and the site preview |

The English reference lies in `docs/*.md`, one file per section, linked from the README. It is written together with the code and describes the API routes, the refusal codes and how the parts work; this site retells it for an administrator.

## Submodules

Both submodules are forks in `bor-project`, pinned to a commit:

```
git submodule update --init --recursive
```

| Submodule | What for |
|---|---|
| `amneziawg/amneziawg-linux-kernel-module` | `src/uapi/wireguard.h` is the source of the C# constants, and the module tree itself for kernels the Amnezia PPA lacks |
| `wstunnel/wstunnel` | carries the UDP of an interface inside WebSocket |

After the module submodule moves, the constants are regenerated: `python3 tools/generate-uapi.py`.

Building and loading the module from the tree:

```
make -C amneziawg/amneziawg-linux-kernel-module/src -j"$(nproc)"
sudo cp amneziawg/amneziawg-linux-kernel-module/src/amneziawg.ko /lib/modules/"$(uname -r)"/updates/
sudo depmod -a && sudo modprobe amneziawg
```

`insmod` on the built file fails with `Unknown symbol`: the module needs `libcurve25519` and `udp_tunnel`, and only `modprobe` pulls them in. The `amneziawg-tools` package is not needed: the server talks to the kernel over netlink itself, and `ip link add dev <name> type amneziawg` needs iproute2 alone.

`wstunnel` builds like this:

```
cargo build --release --manifest-path wstunnel/wstunnel/Cargo.toml -p wstunnel-cli
```

The package takes a static musl build of it (`x86_64-unknown-linux-musl` or `aarch64-unknown-linux-musl`), so that the binary runs on any glibc of the host: this needs the target (`rustup target add x86_64-unknown-linux-musl`) and `musl-gcc` from `musl-tools`. The image builds its own against the glibc of the image.

## Building

You need the .NET 10 SDK and Node.js 22.

```
dotnet build AmneziaGeo.Server.slnx
npm --prefix amneziageo-web install
npm --prefix amneziageo-web run build
```

The build of the panel puts it into `wwwroot` of the API project, and the server serves it from the same port.

## Running

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Api
```

The panel is at `http://<address>:8443/`. While the interface is being edited, Vite runs next to it: it reloads the page on every change and passes `/api` to the server.

```
npm --prefix amneziageo-web run dev
```

| Address | What answers |
|---|---|
| `http://<host>:8443/` | the server with the built interface |
| `http://<host>:5173/` | Vite, `/api` goes to port 8443 |

The first administrator is made from the console, once:

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Cli -- init
```

The same utility reads and changes the interfaces in the kernel; reading and writing want `CAP_NET_ADMIN`:

```
sudo dotnet run --project amneziageo-server/AmneziaGeo.Server.Cli -- device show
```

The data paths are set by the variables `AMNEZIAGEO_DB` (the database, `/var/lib/amneziageo-server/server.db` by default) and `AMNEZIAGEO_SIGNING_KEY` (the token signing key, `/etc/amneziageo-server/signing.pem`).

The strings of the interface lie in `amneziageo-web/src/i18n/en.ts` and `ru.ts`; a string added without its Russian pair breaks the build. The server answers a refusal with a code, not a phrase, and the panel translates the code itself.

## Package and image

```
./deploy/publish.sh
```

It builds the panel, publishes the server and the utility for `linux-x64` with the runtime inside and writes `out/amneziageo-server.tar.gz`, about 50 MB; `./deploy/publish.sh --ui` builds the web interface alone into `out/amneziageo-web.tar.gz`. The package goes on the host with its `install.sh`, see [Installation](../package/install.md#by-hand).

The image for amd64 and arm64:

```
docker buildx build -f deploy/docker/Dockerfile --platform linux/amd64,linux/arm64 -t amneziageo-server:<version> .
```

The layers of the image go from those that change rarely to those every release changes, so an update pulls only the top ones.

## Releases

A release is a `vX.Y.Z.W` tag on a commit of the `master` branch; a tag outside `master` builds nothing. The workflow `.github/workflows/release.yml` builds packages for x64 and arm64, next to each of them its files one by one (`.pack`) and their list (`.files`), the image `ghcr.io/bor-project/amneziageo-server` for both platforms and the manifest `update.json` with the version, the image by digest and the digests of the packages; `update.json.sig` signs the manifest with the release key. A tag with a fourth number above zero or with a suffix makes a pre-release. The panel checks the signature of the manifest with the key it carries inside and offers a release only with a valid signature, see [Updates](../package/panel/overview.md#updates).

## This site

Jekyll builds the site from `docs/` the same way as the client site: the shared text of the pages lies in `docs/_includes/en/` and `docs/_includes/ru/`, the wrapper pages for each way of installation and language (`<topic>.md` and `<topic>.ru.md`) in `docs/package/` and `docs/docker/`, the table of contents in `docs/_data/toc.yml`, the ways of installation in `docs/_data/kinds.yml`. Text that differs branches on Liquid conditions by the `page.kind` field of the wrapper page (`package` or `docker`). Screenshots come in through `shot.html` from `docs/assets/img/<language>/`; when there is no shot in the language of the page, the English one is taken, and the check counts it.

Preview: `tools/docs-site.sh serve` builds the site as GitHub Pages does and serves it at `http://127.0.0.1:4001/amneziageo_server/`, rebuilding on save; in VS Code this is F5 "Docs Site". On Windows the script runs in WSL, takes the `github-pages` and `webrick` gems from `~/ag-docs` (the `AG_DOCS_GEMS` variable) and keeps the built site in `~/ag-server-docs` (`AG_SERVER_DOCS_HOME`). `tools/docs-site.sh check` checks the built site: links, anchors, Liquid leftovers, placeholders, shots in another language and long dashes in the sources.
