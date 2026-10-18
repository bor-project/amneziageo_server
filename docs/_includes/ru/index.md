# {{ page.title }}
{% if page.kind == "docker" %}
Панель работает контейнером из образа `ghcr.io/bor-project/amneziageo-server` в сети хоста: интерфейсы туннеля, правила брандмауэра и порты у неё те же, что были бы у пакета. На хосте остаются модуль `amneziawg`, Docker с compose и каталоги с базой и настройками; сама панель, утилита, WebSocket-прокси и консольное меню едут в образе. Обновление - новый образ, откат - прежний тег.
{% else %}
Панель работает службой systemd `amneziageo-server` от root из `/opt/amneziageo-server`: релизы лежат рядом, ссылка `current` указывает на рабочий, `previous` - на предыдущий для отката. База в `/var/lib/amneziageo-server`, ключ подписи токенов и переменные запуска в `/etc/amneziageo-server`. Фронты WebSocket интерфейсов - службы `amneziageo-proxy@<интерфейс>`.
{% endif %}
## С чего начать

1. [Установка](install.ru.html)
2. [Первый запуск](first-run.ru.html)

## Консольное меню

[Консольное меню](menu.ru.html) `amneziageo-server`: установка, обновление и откат, копии базы, адрес, порт и путь панели, пользователи и токены, служба и её журнал, сертификаты, брандмауэр, интерфейсы, фронты WebSocket, BBR и DNS для клиентов.

## Панель

{% include children.html of="panel/index" %}

## Рецепты

{% include children.html of="recipes/index" %}

## Утилита

[Утилита](cli.ru.html): команды `amneziageo-server <команда>` для консоли и скриптов, [HTTP API](../api.ru.html) для всего остального.
