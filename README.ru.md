# AmneziaGeo Server

[English](README.md) | **Русский**

Сервер AmneziaGeo - панель управления сервером AmneziaWG на Linux. Она сама поднимает интерфейсы туннеля в ядре, ведёт клиентов и выдаёт им конфигурации файлом, QR-кодом, ссылкой и подпиской, считает трафик, а трафик клиентов умеет направлять дальше: по гео-базам и доменам через другие серверы, через сам хост или в блок. Работает на Debian и Ubuntu с модулем `amneziawg` в ядре, службой или в Docker, на amd64 и arm64.

**[Скачать](https://github.com/bor-project/amneziageo_server/releases) · [Документация](https://bor-project.github.io/amneziageo_server/index.ru.html) · [Клиент](https://github.com/bor-project/amneziageo/blob/master/README.ru.md)**

## Возможности

- Интерфейсы AmneziaWG со всей обфускацией профиля 3.1: панель кладёт их в ядро сама, без `awg-quick`; интерфейсы, поднятые `awg-quick`, переходят в панель вместе с клиентами.
- Клиенты с файлом конфигурации, QR-кодом, ссылкой и подпиской; шаблоны собирают `AllowedIPs` из гео-ключей, сетей и доменов.
- Маршрутизация на сервере по странам, категориям сайтов, доменам, адресам, портам и клиентам: через другие серверы (AmneziaWG, в том числе внутри WebSocket), через хост или в блок.
- Резолвер для клиентов по HTTPS или TLS: так правила по доменам работают на уровне ядра.
- Туннель внутри WebSocket на TCP-порту интерфейса для сетей, где проходит только веб-трафик.
- Обновления из панели с откатом при сбое, резервные копии базы и консольное меню `amneziageo-server` на хосте.

Установка [службой](https://bor-project.github.io/amneziageo_server/service/index.ru.html) или [в Docker](https://bor-project.github.io/amneziageo_server/docker/index.ru.html), настройка и обслуживание - на **[сайте документации](https://bor-project.github.io/amneziageo_server/index.ru.html)**.

## Клиент

**[Клиент AmneziaGeo](https://github.com/bor-project/amneziageo/blob/master/README.ru.md)** для Windows, Linux и Android сам читает с сервера гео-источники, списки маршрутизации, адрес подписки и замер скорости: подробности на его **[сайте документации](https://bor-project.github.io/amneziageo/index.ru.html)**. Любое другое приложение AmneziaWG работает с сервером как с обычным сервером AmneziaWG.

## Разработчикам

Сборка, устройство репозитория, подмодули и выпуски: [для разработчика](https://bor-project.github.io/amneziageo_server/build.ru.html), [HTTP API](https://bor-project.github.io/amneziageo_server/api.ru.html).

Справочник по разделам на английском, он пишется вместе с кодом: список в [README.md](README.md#for-developers).

## Благодарности

Сервер построен на экосистеме [Amnezia VPN](https://github.com/amnezia-vpn): модуль ядра AmneziaWG. Туннель внутри WebSocket несёт [wstunnel](https://github.com/erebe/wstunnel). Гео-базы по умолчанию: [Loyalsoldier/v2ray-rules-dat](https://github.com/Loyalsoldier/v2ray-rules-dat), [runetfreedom](https://github.com/runetfreedom), [zkeen-ip](https://github.com/jameszeroX/zkeen-ip) и наша [amneziageo-geo](https://github.com/bor-project/amneziageo-geo).
