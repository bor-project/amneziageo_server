---
title: HTTP API
lang: ru
topic: api
permalink: /api.ru.html
description: Маршруты панели под /api, токены для скриптов, вид ответов и описание OpenAPI.
---

# HTTP API

Панель делает всё через HTTP API под `/api`, и скрипт зовёт те же маршруты. Каждый маршрут, кроме `POST /api/auth/login`, `POST /api/auth/refresh` и `GET /api/health`, требует токен в заголовке `Authorization: Bearer`.

## Токены

- **Токен сессии.** `POST /api/auth/login` с телом `{ "user": "...", "password": "..." }` отдаёт пару: токен доступа на десять минут и токен обновления, который `POST /api/auth/refresh` меняет на новую пару. На этих токенах работает сама панель, с правами роли учётной записи.
- **Токен для скриптов.** Долгоживущий токен начинается с `agt_`, действует от имени роли и показывается один раз: выпускается в таблице «Токены» раздела «Настройки» > «Пользователи и роли» или командой `amneziageo-server token add <название> --role <роль> [--days <n>]`. Смена прав роли сразу действует на её токены; отключение и удаление учётных записей токена не касаются. Отзывается там же или командой `token revoke`.

Оба токена идут в одном заголовке:

```
curl -H "Authorization: Bearer agt_..." https://panel.example.com:8443/api/clients
```

Если панель стоит под своим путём, он идёт перед `/api`: `https://panel.example.com:8443/<путь>/api/clients`.

## Ответы

Отказ приходит как `{ "error": "<код>", "message": "<слова>" }` со статусом по смыслу: 400 на запрос, который не прошёл проверку, 401 без токена или с истёкшим (`unauthorized`), 403 на право, которого у роли нет (`forbidden`), 404 на то, чего нет, 409 на столкновение. Неизвестный маршрут отвечает 404 с `unknown-route`. Коды отказов каждого раздела перечислены на страницах панели, там же их перевод, который показывает панель.

Каждый `POST /api/<раздел>/{id}/switch` принимает `{ "on": true }` или `{ "on": false }`; тело без `on` отвечает 400 и `incomplete`.

## Описание OpenAPI

`GET /api/openapi.json` отдаёт документ OpenAPI 3.1 со всеми маршрутами: путь, метод, параметры и форма тела запроса. Требование безопасности маршрута называет право, которое он просит. Документ запрашивается с токеном, как любой маршрут. Форм ответов в нём нет: их описывают страницы ниже.

## Маршруты по разделам

| Раздел | Страница сайта | Справка репозитория с маршрутами |
|---|---|---|
| учётные записи, роли, токены | [Пользователи и роли](../service/panel/users.ru.md) | [auth.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/auth.md) |
| интерфейсы | [Интерфейсы](../service/panel/interfaces.ru.md) | [configs.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/configs.md) |
| клиенты | [Клиенты](../service/panel/clients.ru.md) | [clients.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/clients.md), [devices.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/devices.md) |
| шаблоны и пресеты маршрутизации | [Шаблоны](../service/panel/templates.ru.md), [Пресеты маршрутизации](../service/panel/presets.ru.md) | [templates.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/templates.md) |
| подписки | [Подписки](../service/panel/subscriptions.ru.md) | [subscriptions.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/subscriptions.md) |
| каналы и группы | [Каналы](../service/panel/channels.ru.md) | [outbounds.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/outbounds.md), [balancers.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/balancers.md) |
| правила, базовые списки, проверка | [Правила](../service/panel/rules.ru.md), [Базовые](../service/panel/basic.ru.md), [Проверка](../service/panel/test.ru.md) | [rules.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/rules.md) |
| резолвер | [DNS](../service/panel/dns.ru.md) | [dns.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/dns.md) |
| списки Geo | [Списки Geo](../service/panel/geo.ru.md) | [geo.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/geo.md) |
| сервисы интерфейса: hello, скорость, WebSocket, сигнал | [Интерфейсы](../service/panel/interfaces.ru.md#сервисы-на-tcp-порту) | [services.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/services.md) |
| где отвечает панель, сертификат | [Сервер](../service/panel/server.ru.md), [Сертификаты](../service/panel/certificates.ru.md) | [serving.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/serving.md) |
| порты в брандмауэре хоста | [Интерфейсы](../service/panel/interfaces.ru.md#порты-в-брандмауэре) | [firewall.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/firewall.md) |
| резервные копии базы | [Обзор](../service/panel/overview.ru.md#резервная-копия) | [install.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/install.md#moving-to-another-server) |
| диагностика, журнал подключений | [Диагностика](../service/panel/diagnostics.ru.md) | [diagnostics.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/diagnostics.md) |
| обзор хоста и службы | [Обзор](../service/panel/overview.ru.md) | [overview.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/overview.md) |
| обновления панели | [Обзор](../service/panel/overview.ru.md#обновление) | [updates.md](https://github.com/bor-project/amneziageo_server/blob/master/docs/updates.md) |

Страницы панели одинаковы для службы и Docker, ссылки ведут на вариант службы.

## Что остаётся в консоли

- `init` - первый администратор, до него в панель никто не входит.
- `device` и `family` - ядро как есть, для отладки.

Импорт есть и там, и там: консоль читает файлы хоста, API принимает их текст (`POST /api/configs/import`, `POST /api/clients/import`).

## Пример

Токен на день под роль `admin`, новый клиент на интерфейсе 1 и его файл:

```
amneziageo-server token add nightly --role admin --days 1
```

Последняя строка вывода - сам токен, он печатается один раз.

```
API=https://panel.example.com:8443/api
curl -s -H "Authorization: Bearer agt_..." -H "Content-Type: application/json" \
  -d '{ "name": "anna-phone", "configId": 1 }' "$API/clients"
curl -s -H "Authorization: Bearer agt_..." "$API/clients/7/config"
```

Телу `POST /api/clients` хватает имени и номера интерфейса: ключи и адрес панель подберёт сама. Ответ `GET /api/clients/{id}/config` несёт текст файла, ссылку `vpn://` и адрес подписки.
