---
title: Для разработчика
lang: ru
topic: develop/build
permalink: /build.ru.html
description: Устройство репозитория, сборка сервера, панели и подмодулей, пакет и образ, релизы, сайт документации.
---

# Для разработчика

Репозиторий [bor-project/amneziageo_server](https://github.com/bor-project/amneziageo_server). Сервер на .NET, панель на React, модуль ядра и WebSocket-прокси подмодулями.

## Что где

| Путь | Что |
|---|---|
| `amneziageo-server/AmneziaGeo.Server.Core` | типы предметной области, интерфейсы, правила |
| `amneziageo-server/AmneziaGeo.Server.Dal` | EF Core над SQLite, хранилища учётных записей и миграции |
| `amneziageo-server/AmneziaGeo.Server.Awg` | управление ядром через generic netlink |
| `amneziageo-server/AmneziaGeo.Server.Auth` | учётные записи, роли как утверждения, пароли и токены |
| `amneziageo-server/AmneziaGeo.Server.Api` | HTTP API и собранный веб-интерфейс |
| `amneziageo-server/AmneziaGeo.Server.Cli` | консольная утилита |
| `amneziageo-web` | интерфейс панели, React и Vite |
| `amneziageo-tests` | тесты |
| `amneziawg` | модуль ядра подмодулем |
| `wstunnel` | WebSocket-прокси подмодулем |
| `deploy` | служба systemd, установщик пакета, консольное меню, контейнер |
| `docs` | справка на английском по разделам и этот сайт |
| `tools` | генератор констант netlink и просмотр сайта |

Справка на английском лежит в `docs/*.md`, по файлу на раздел, со ссылками из README. Она пишется вместе с кодом и описывает маршруты API, коды отказов и устройство частей; этот сайт пересказывает её для администратора.

## Подмодули

Оба подмодуля - форки в `bor-project`, закреплены коммитом:

```
git submodule update --init --recursive
```

| Подмодуль | Зачем |
|---|---|
| `amneziawg/amneziawg-linux-kernel-module` | `src/uapi/wireguard.h` - источник констант C#, и само дерево модуля для ядер, которых нет в PPA Amnezia |
| `wstunnel/wstunnel` | переносит UDP интерфейса внутри WebSocket |

После сдвига подмодуля модуля константы пересобираются: `python3 tools/generate-uapi.py`.

Собрать и загрузить модуль из дерева:

```
make -C amneziawg/amneziawg-linux-kernel-module/src -j"$(nproc)"
sudo cp amneziawg/amneziawg-linux-kernel-module/src/amneziawg.ko /lib/modules/"$(uname -r)"/updates/
sudo depmod -a && sudo modprobe amneziawg
```

`insmod` на собранный файл падает с `Unknown symbol`: модулю нужны `libcurve25519` и `udp_tunnel`, их подтягивает только `modprobe`. Пакет `amneziawg-tools` не нужен: сервер говорит с ядром по netlink сам, а `ip link add dev <имя> type amneziawg` требует только iproute2.

`wstunnel` собирается так:

```
cargo build --release --manifest-path wstunnel/wstunnel/Cargo.toml -p wstunnel-cli
```

Пакет берёт его статической сборкой под musl (`x86_64-unknown-linux-musl` или `aarch64-unknown-linux-musl`), чтобы бинарник шёл на любой glibc хоста: нужны цель (`rustup target add x86_64-unknown-linux-musl`) и `musl-gcc` из `musl-tools`. Образ собирает свой под glibc образа.

## Сборка

Нужны .NET 10 SDK и Node.js 22.

```
dotnet build AmneziaGeo.Server.slnx
npm --prefix amneziageo-web install
npm --prefix amneziageo-web run build
```

Сборка панели кладёт её в `wwwroot` проекта API, и сервер отдаёт её с того же порта.

## Запуск

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Api
```

Панель на `http://<адрес>:8443/`. Пока интерфейс правится, рядом запускается Vite: он перезагружает страницу на каждом изменении и передаёт `/api` серверу.

```
npm --prefix amneziageo-web run dev
```

| Адрес | Что отвечает |
|---|---|
| `http://<хост>:8443/` | сервер с собранным интерфейсом |
| `http://<хост>:5173/` | Vite, `/api` уходит на порт 8443 |

Первый администратор заводится из консоли, один раз:

```
dotnet run --project amneziageo-server/AmneziaGeo.Server.Cli -- init
```

Интерфейсы в ядре читает и меняет та же утилита, чтение и запись хотят `CAP_NET_ADMIN`:

```
sudo dotnet run --project amneziageo-server/AmneziaGeo.Server.Cli -- device show
```

Пути данных задают переменные `AMNEZIAGEO_DB` (база, по умолчанию `/var/lib/amneziageo-server/server.db`) и `AMNEZIAGEO_SIGNING_KEY` (ключ подписи токенов, `/etc/amneziageo-server/signing.pem`).

Строки интерфейса лежат в `amneziageo-web/src/i18n/en.ts` и `ru.ts`; строка, добавленная без русской пары, ломает сборку. Сервер отвечает на отказ кодом, а не фразой, и панель переводит код сама.

## Пакет и образ

```
./deploy/publish.sh
```

Собирает панель, публикует сервер и утилиту для `linux-x64` со средой внутри и пишет `out/amneziageo-server.tar.gz`, около 50 МБ; `./deploy/publish.sh --ui` собирает один веб-интерфейс в `out/amneziageo-web.tar.gz`. Пакет ставится на хост его `install.sh`, см. [Установка](../package/install.ru.md#вручную).

Образ для amd64 и arm64:

```
docker buildx build -f deploy/docker/Dockerfile --platform linux/amd64,linux/arm64 -t amneziageo-server:<версия> .
```

Слои образа идут от тех, что меняются реже, к тем, что меняет каждый релиз, поэтому обновление тянет только верхние.

## Релизы

Релиз - тег `vX.Y.Z.W` на коммите ветки `master`; тег вне `master` ничего не собирает. Workflow `.github/workflows/release.yml` собирает пакеты для x64 и arm64, рядом с каждым его файлы по одному (`.pack`) и их список (`.files`), образ `ghcr.io/bor-project/amneziageo-server` для обеих платформ и манифест `update.json` с версией, образом по дайджесту и дайджестами пакетов; `update.json.sig` подписывает манифест ключом релизов. Тег с четвёртым числом больше нуля или с суффиксом даёт предварительный выпуск. Панель проверяет подпись манифеста ключом, который несёт в себе, и предлагает релиз только с верной подписью, см. [Обновление](../package/panel/overview.ru.md#обновление).

## Этот сайт

Сайт собирает Jekyll из `docs/` так же, как сайт клиента: общий текст страниц лежит в `docs/_includes/ru/` и `docs/_includes/en/`, страницы-обёртки для каждого способа установки и языка (`<тема>.ru.md` и `<тема>.md`) - в `docs/package/` и `docs/docker/`, оглавление - в `docs/_data/toc.yml`, способы установки - в `docs/_data/kinds.yml`. Текст, который различается, ветвится условиями Liquid по полю `page.kind` страницы-обёртки (`package` или `docker`). Кадры экрана подключает `shot.html` из `docs/assets/img/<язык>/`; кадра на языке страницы нет - берётся английский, и проверка это считает.

Просмотр: `tools/docs-site.sh serve` собирает сайт как GitHub Pages и отдаёт его на `http://127.0.0.1:4001/amneziageo_server/`, пересобирая при сохранении; в VS Code это F5 «Docs Site». На Windows скрипт идёт в WSL, гемы `github-pages` и `webrick` берёт из `~/ag-docs` (переменная `AG_DOCS_GEMS`), собранный сайт держит в `~/ag-server-docs` (`AG_SERVER_DOCS_HOME`). `tools/docs-site.sh check` проверяет собранный сайт: ссылки, якоря, остатки Liquid, заглушки, кадры на чужом языке и длинные тире в исходниках.
