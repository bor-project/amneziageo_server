# Панель AmneziaGeo на Debian 12 в Docker

Панель идёт готовым образом из GitHub Container Registry и работает контейнером в сети хоста: интерфейсы
AmneziaWG, правила брандмауэра и порты у неё те же, что у хоста. Модуль `amneziawg` остаётся на хосте, образ
несёт только панель, поэтому сначала ставится модуль, потом Docker, потом контейнер. Все команды выполняются от
root.

Нужно: Debian 12 (bookworm) amd64 или arm64, публичный адрес, от 1 ГБ памяти (меньше - временный swap на шаге 3),
около 2 ГБ свободного диска под образ и слои.

Установка пакетом, без Docker, описана в [install-debian12.md](install-debian12.md). Панель одна и та же, разница
только в том, чем она запускается.

## Коротко: меню `amneziageo-server`

Весь путь ниже делает одна команда. Скрипт `amneziageo-server` из релиза сначала осматривает хост и печатает,
что на нём есть: система и ядро, ждущие обновления, модуль `amneziawg`, память и диск, Docker и его контейнеры,
пересылка и BBR, ufw, занятые порты с именами служб, интерфейсы AmneziaWG и WireGuard, сертификаты Let's Encrypt.
Потом задаёт все вопросы сразу, с ответами по умолчанию из осмотра, показывает план и после согласия:

- обновляет систему (шаг 1); если пришло новое ядро, перезагружает хост, и та же команда после перезагрузки
  продолжает с теми же ответами;
- ставит модуль из PPA Amnezia, если ядро его не несёт, включает пересылку и BBR (шаги 2-4);
- ставит Docker из репозитория Docker и дописывает `"ip-forward-no-drop": true` в `/etc/docker/daemon.json`
  (шаг 5); Docker перезапускается, только пока он отбрасывает пересылаемые пакеты, а при чужих контейнерах - с
  согласия;
- пишет `/opt/amneziageo-docker/compose.yaml` как на шаге 6 с образом последнего релиза и заводит первого
  администратора с временным паролем (шаг 7);
- выпускает сертификат Let's Encrypt на имя сервера или берёт уже выпущенный на это имя и открывает панель на всех
  адресах хоста (шаг 8);
- заводит первый интерфейс и клиента: конфигурация клиента ложится в `/root`, по желанию и QR-кодом (шаг 10);
- открывает нужные порты в ufw или включает его, оставляя открытыми ssh и то, что уже слушает (шаг 11);
- проверяет панель, сертификат и интерфейс (шаг 9) и печатает адрес панели, логин и временный пароль.

```bash
curl -fsSL https://github.com/bor-project/amneziageo_server/releases/latest/download/amneziageo-server \
  -o /usr/local/bin/amneziageo-server
chmod 755 /usr/local/bin/amneziageo-server
amneziageo-server install
```

На вопрос `Install as` ответьте `2` (Docker), на `Channel` - `1` (обычные релизы) или `2` (беты). Ответы можно
заранее положить в файл строками `ключ=значение` и дать его командой `amneziageo-server install --answers <файл>`:
тогда скрипт спрашивает только то, чего в файле нет. Ключи перечислены в [install.md](../install.md#the-menu).

Перенос с другого сервера: на старой панели «Обзор» > «Скачать бэкап», файл скопировать на новый хост и поставить
панель командой `amneziageo-server install --restore <файл>` (или назвать файл на вопросе `Backup file to restore`).
Скрипт проверит файл, поставит панель и запустит её на базе из бэкапа: учётки и токены, интерфейсы с ключами,
клиенты, правила и настройки панели переезжают как есть, свежая база уходит в `/var/lib/amneziageo-server/backup`.
Клиенты подключаются по имени сервера из своих конфигураций, поэтому имя в DNS переводится на новый хост. Сертификат
панели, ключ подписи и файлы гео в бэкап не входят: сертификат на то же имя скрипт предложит выпустить, когда имя
уже указывает на новый хост (иначе панель ответит без сертификата до пункта 22), адреса старого хоста, на которых
слушала панель, отбросит, а источники гео панель скачает сразу после запуска. На работающей панели то же делает
`amneziageo-server restore <файл>` или пункт 5 > 4 меню.

Потом `amneziageo-server` без аргументов открывает меню, в том числе по ssh: обновление и откат, копии базы,
адрес, порт и путь панели, пользователи и токены, служба и её журнал, сертификаты, брандмауэр, интерфейсы, фронты
WebSocket и BBR. Пункты работают и командами: `amneziageo-server update`, `rollback`, `status`, `log`, `bbr on`;
прочие команды уходят в утилиту панели, например `amneziageo-server user list`. Полный список -
`amneziageo-server help`.

То же меню открывается в самом контейнере, как бы он ни был поставлен: `docker compose exec panel amneziageo-server`.
Остановка, запуск и перезапуск держат или запускают заново панель внутри контейнера, сам контейнер работает дальше
и сессия не рвётся. Автозапуск включает и выключает политику перезапуска контейнера, журнал - журнал контейнера.
Обновление переводит контейнер на новый образ и сессию завершает. Пункты, которые меняют сам хост (откат на старый
образ, удаление, сертификат Let's Encrypt), отвечают, что из контейнера до хоста не достать. Правила ufw, включение
и выключение ufw, BBR и пересылка вместо этого печатают готовую команду для хоста. Команды утилиты идут через то же
имя: `docker compose exec panel amneziageo-server ...` из шагов ниже
работают как прежде.

Дальше тот же путь описан по шагам, руками.

## 1. Обновить систему и перезагрузиться

```bash
apt update && apt full-upgrade -y
reboot
```

Модуль собирается под ядро, заголовки которого стоят. Если обновление принесло новое ядро, без перезагрузки модуль
соберётся под новое и не загрузится в старом. После перезагрузки `uname -r` показывает ядро, под которое будем
собирать.

## 2. Заголовки ядра и сборочные пакеты

```bash
apt install -y --no-install-recommends curl ca-certificates nftables dkms gcc make \
  linux-headers-$(uname -r) linux-headers-$(uname -r | cut -d- -f3-)
ls -d /lib/modules/$(uname -r)/build
```

- `linux-headers-$(uname -r)` - заголовки работающего ядра.
- `linux-headers-$(uname -r | cut -d- -f3-)` - мета-пакет своего вида ядра (`amd64`, `cloud-amd64`, `arm64`): с
  ним DKMS сам пересоберёт модуль после следующих обновлений ядра.

`ls` должен показать каталог `build`, без него модуль не соберётся. `nftables` здесь нужен, чтобы смотреть
политику пересылки на хосте (шаг 5); свои таблицы панель пишет изнутри образа.

## 3. Модуль AmneziaWG из PPA Amnezia

PPA собран для Ubuntu, из него берём только `amneziawg-dkms`: это исходники модуля, DKMS собирает их на месте. Так
поставлены наши серверы на Debian 13 (серия `noble`). Если на Debian 12 apt откажет по зависимостям, поменяйте
`Suites: noble` на `Suites: jammy`.

Ключ PPA, отпечаток `75C9 DD72 C799 870E 3105 42E2 4166 F2C2 5729 0828` («Launchpad PPA for Iurii Egorov»):

```bash
install -d -m 755 /etc/apt/keyrings
curl -fsSL 'https://keyserver.ubuntu.com/pks/lookup?op=get&options=mr&search=0x75C9DD72C799870E310542E24166F2C257290828' \
  -o /etc/apt/keyrings/amnezia-ppa.asc
cat > /etc/apt/sources.list.d/amnezia-ppa.sources <<'EOF'
Types: deb
URIs: https://ppa.launchpadcontent.net/amnezia/ppa/ubuntu/
Suites: noble
Components: main
Signed-By: /etc/apt/keyrings/amnezia-ppa.asc
EOF
apt update
```

При памяти меньше 1 ГБ сборка может упасть от нехватки памяти. Тогда перед установкой включите временный swap:

```bash
fallocate -l 512M /swap-build && chmod 600 /swap-build && mkswap /swap-build && swapon /swap-build
```

Установка и загрузка модуля:

```bash
apt install -y --no-install-recommends amneziawg-dkms
dkms status
echo amneziawg > /etc/modules-load.d/amneziawg.conf
modprobe amneziawg
lsmod | grep amneziawg
modinfo -F version amneziawg
```

`dkms status` должен показать `amneziawg/<версия>, <ядро>, <архитектура>: installed`. Если модуля для текущего
ядра нет: `dkms autoinstall -k $(uname -r)`, причина ошибки сборки в `/var/lib/dkms/amneziawg/*/build/make.log`.

Временный swap после сборки убрать:

```bash
swapoff /swap-build && rm -f /swap-build
```

`amneziawg-tools` (`awg`, `awg-quick`) не ставим: панель работает с модулем сама. Интерфейсы без `awg` показывает
консоль панели (шаг 10).

## 4. Пересылка пакетов

```bash
cat > /etc/sysctl.d/90-amneziageo.conf <<'EOF'
net.ipv4.ip_forward = 1
net.ipv6.conf.all.forwarding = 1
EOF
sysctl --system
```

Контейнер не пишет в `/proc/sys` хоста, поэтому пересылку включает хост. Без неё интерфейсы не поднимаются, и
панель отвечает `forwarding is off in /proc/sys/...`. Если IPv6 хоста настраивается автоматически (SLAAC),
добавьте в тот же файл `net.ipv6.conf.<внешний интерфейс>.accept_ra = 2`: с включённой пересылкой ядро перестаёт
принимать объявления маршрутизатора, и хост теряет IPv6-маршрут.

## 5. Docker и политика пересылки

Пакеты Docker берём из репозитория самого Docker, ключ с их сайта:

```bash
apt install -y --no-install-recommends ca-certificates curl
install -d -m 755 /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
cat > /etc/apt/sources.list.d/docker.sources <<'EOF'
Types: deb
URIs: https://download.docker.com/linux/debian
Suites: bookworm
Components: stable
Signed-By: /etc/apt/keyrings/docker.asc
EOF
apt update
apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
docker compose version
```

Docker при старте переводит политику цепочки `FORWARD` в `DROP`, и тогда клиенты туннеля не выходят дальше
сервера. Демон оставляет политику в покое так:

```bash
cat > /etc/docker/daemon.json <<'EOF'
{
  "ip-forward-no-drop": true
}
EOF
systemctl restart docker
nft list chain ip filter FORWARD 2>/dev/null | head -3
```

В первой строке вывода не должно быть `policy drop`. Если на хосте уже есть свой `/etc/docker/daemon.json`,
добавьте в него только строку `"ip-forward-no-drop": true`, остальное сохраните.

## 6. Панель контейнером

Версия образа - это версия релиза: `0.0.3.0` - последний обычный релиз, `0.0.5.0` - последняя бета. Тег `latest`
ведёт на последний обычный релиз. Образ собран для amd64 и arm64, нужную платформу Docker берёт сам.

```bash
VER=0.0.3.0
mkdir -p /opt/amneziageo-docker /var/lib/amneziageo-server /etc/amneziageo-server /etc/amnezia/amneziawg
cd /opt/amneziageo-docker
printf 'AMNEZIAGEO_TAG=%s\n' "$VER" > .env
cat > compose.yaml <<'EOF'
name: amneziageo-server

services:
  panel:
    image: ghcr.io/bor-project/amneziageo-server:${AMNEZIAGEO_TAG:-latest}
    network_mode: host
    cap_add:
      - NET_ADMIN
    env_file:
      - path: server.env
        required: false
    volumes:
      - /var/lib/amneziageo-server:/var/lib/amneziageo-server
      - /etc/amneziageo-server:/etc/amneziageo-server
      - /etc/amnezia/amneziawg:/etc/amnezia/amneziawg
      - /var/run/docker.sock:/var/run/docker.sock
    logging:
      driver: json-file
      options:
        max-size: 10m
        max-file: "3"
    restart: unless-stopped
EOF
docker compose pull
docker compose up -d
docker compose ps
```

Что здесь важно:

- `network_mode: host` и `NET_ADMIN` - панель поднимает интерфейсы и пишет правила в ядре хоста, поэтому сеть у
  неё хостовая, а не своя.
- каталоги хоста вместо томов: база и ключ подписи лежат на виду, копировать и возвращать их просто.
- `/var/run/docker.sock` - через него панель обновляет сама себя кнопкой, пересоздавая свой контейнер на новом
  образе. Без сокета обновление остаётся ручным (шаг 12).
- версия берётся из `.env` рядом с `compose.yaml`: при обновлении кнопкой панель пишет туда новый тег, и
  `docker compose up -d` руками потом поднимает ту же версию.
- `server.env` рядом с `compose.yaml` не обязателен, он появится на шаге 8.

## 7. Первый администратор

```bash
cd /opt/amneziageo-docker
docker compose exec panel amneziageo-server init --user admin
```

Утилита спросит пароль. С ключом `--generate` она придумает пароль сама и напечатает его один раз
(`password: ...`), сохраните его. `init` работает, только пока в панели нет включённого администратора. Ключ
`--user` в контейнере обязателен: учётки хоста в него не заходят.

Последней строкой утилита называет порт и путь панели (`the panel answers on port 8443 under /sub/...`).

Пароль выдаётся временным: при первом входе панель попросит его сменить. Ключ `--permanent` оставляет пароль как
есть.

## 8. Где панель отвечает

По умолчанию контейнер слушает только `127.0.0.1:8443` без TLS, в сети хоста панели не видно. Два варианта.

Адрес и порт панель берёт из `server.env` только до первого запуска: на первом старте она записывает их в свою
базу и дальше читает оттуда. Так же их записывает команда `amneziageo-server panel`, данная до первого запуска.
Поэтому вариант Б выбирают до первого `docker compose up -d`. Если контейнер уже
работал, адрес меняют в самой панели: «Настройки» > «Сервер» > «Слушать на адресах» > «Все адреса», и
`server.env` на него больше не влияет.

На первом старте панель придумывает себе путь вида `/sub/<16 букв и цифр>/` и пишет его в журнал контейнера:

```bash
docker compose -f /opt/amneziageo-docker/compose.yaml logs | grep "the panel answers"
```

Путь нужен для входа, дальше он виден в «Настройки» > «Сервер» > «Путь». Чтобы панель стояла в корне, до
первого запуска добавьте в `server.env` строку `Web__Path=/`.

**А. Через SSH-туннель, без сертификата**

Контейнер уже запущен шагом 6. На своём компьютере:

```bash
ssh -N -L 8443:127.0.0.1:8443 root@<адрес сервера>
```

и в браузере `http://localhost:8443/sub/<путь из журнала>/`.

**Б. В сети по TLS с сертификатом Let's Encrypt**

### 8.1. Готовое имя хоста и доступность сервера

Предполагаем, что имя уже создано (например, `my-panel.ddns.net` в No-IP) и его A-запись указывает на публичный
IPv4 сервера. Сертификат выпускаем на хосте, а не в контейнере; замените пример своим именем, без `https://`,
порта и пути.

```bash
D=my-panel.ddns.net
getent ahostsv4 "$D"
ss -ltnp 'sport = :80'
```

Проверьте, что DNS возвращает адрес сервера, а TCP 80 свободен. Если есть AAAA-запись, она тоже должна вести на
этот сервер с доступным IPv6; неверную AAAA-запись исправьте или удалите.

Для проверки HTTP-01 входящий TCP 80 должен быть доступен из интернета при выпуске и каждом продлении.
Откройте его в брандмауэре сервера и у провайдера; при NAT нужен проброс внешнего TCP 80 на TCP 80 сервера.
Для входа в панель нужен TCP 8443. Если используется UFW, до выпуска сертификата выполните:

```bash
apt install -y ufw
ufw allow 80/tcp
ufw allow 8443/tcp
```

Если TCP 80 уже занят nginx, Apache или другой службой, этот вариант `--standalone` без дополнительной настройки
не подойдёт: используйте соответствующий плагин Certbot или `--webroot`. Однократное освобождение порта решает
только первый выпуск, но не последующие продления.

### 8.2. Установка Certbot и выпуск сертификата

```bash
D=my-panel.ddns.net
apt update
apt install -y certbot
certbot certonly --standalone --preferred-challenges http --cert-name "$D" -d "$D"
certbot certificates
```

Certbot спросит почту и согласие с условиями Let's Encrypt. После успешного выпуска появятся:

- `/etc/letsencrypt/live/$D/fullchain.pem` - сертификат и промежуточная цепочка;
- `/etc/letsencrypt/live/$D/privkey.pem` - закрытый ключ.

### 8.3. Подключение сертификата к панели

Сертификат читается внутри контейнера, поэтому каталог Let's Encrypt отдаётся ему томом. Проверка здоровья идёт
за панелью сама: при старте панель пишет адрес проверки со схемой и портом в `/var/lib/amneziageo-server/health`.

```bash
D=my-panel.ddns.net
cd /opt/amneziageo-docker
cat >> server.env <<EOF
Web__Listen__0=*:8443
Web__Certificate=/etc/letsencrypt/live/$D/fullchain.pem
Web__CertificateKey=/etc/letsencrypt/live/$D/privkey.pem
EOF
chmod 600 server.env
cat > compose.override.yaml <<'EOF'
services:
  panel:
    volumes:
      - /etc/letsencrypt:/etc/letsencrypt:ro
EOF
docker compose up -d
docker compose ps
```

Строки дописываются в конец `server.env`, из одинаковых переменных Docker берёт последнюю. Тома из
`compose.override.yaml` добавляются к томам `compose.yaml`.

Откройте `https://my-panel.ddns.net:8443/`, подставив своё имя. Проверка с сервера с проверкой доверия и имени:

```bash
D=my-panel.ddns.net
curl --fail --show-error --resolve "$D:8443:127.0.0.1" "https://$D:8443/api/health"
```

Используйте пути из `live`, не копируйте PEM-файлы в отдельный каталог: при продлении Certbot обновляет ссылки в
`live`. Обновлённый сертификат панель перечитывает сама, без перезапуска контейнера. Адрес, порт и сертификат
потом меняются в панели: «Настройки», вкладки «Сервер» и «Сертификаты»; её настройки старше `server.env`.

### 8.4. Автоматическое продление

В пакете Debian есть таймер `certbot.timer`. Включите его и проверьте пробное продление:

```bash
D=my-panel.ddns.net
systemctl enable --now certbot.timer
systemctl list-timers --all certbot.timer
certbot renew --cert-name "$D" --dry-run
```

Certbot слушает TCP 80 только во время проверки, контейнер этот порт не занимает. Для продления имя должно
оставаться активным и указывать на сервер, а TCP 80 - оставаться разрешённым извне.

Справка: [Certbot: выпуск и продление](https://eff-certbot.readthedocs.io/en/stable/using.html),
[Let's Encrypt: HTTP-01](https://letsencrypt.org/docs/challenge-types/).

### 8.5. Панель осталась на `127.0.0.1`

Так выходит, если контейнер уже запускался до того, как рядом с `compose.yaml` появился `server.env`: адрес
панель берёт из своей базы, а из `server.env` подхватывает только сертификат. В журнале это видно строкой
`the panel answers from 127.0.0.1:8443 under /`, а `ss -ltnp | grep 8443` на хосте показывает `127.0.0.1:8443`.

Через SSH-туннель панель при этом открывается, и адрес меняется в ней: «Настройки» > «Сервер» > «Слушать на
адресах» > «Все адреса» > «Сохранить».

То же самое с хоста, без браузера: временный токен, настройки с пустым списком адресов, перезапуск. Нужен
`python3`, в Debian 12 он есть.

```bash
cd /opt/amneziageo-docker
api=https://127.0.0.1:8443/api/panel    # без сертификата: http://127.0.0.1:8443/api/panel
out=$(docker compose exec -T panel amneziageo-server token add panelfix --role admin --days 1)
id=$(printf '%s\n' "$out" | sed -n 's/^minted \([0-9]*\) as .*/\1/p')
tok=$(printf '%s\n' "$out" | tail -1)
body=$(curl -sk -H "Authorization: Bearer $tok" "$api" | python3 -c 'import json, sys
d = json.load(sys.stdin)
d["listen"] = []
print(json.dumps({k: d[k] for k in ("listen", "domains", "port", "opened", "path", "certificate", "certificateKey", "language", "prereleases")}))')
curl -sk -X PUT -H "Authorization: Bearer $tok" -H "Content-Type: application/json" -d "$body" "$api"
curl -sk -X POST -H "Authorization: Bearer $tok" "$api/restart"
docker compose exec -T panel amneziageo-server token revoke "$id" --yes
```

Панель кончает работу, и Docker поднимает контейнер заново сам. Через несколько секунд `docker compose ps`
показывает `running`, `ss -ltnp | grep 8443` - `*:8443`, а в журнале появляется
`the panel answers from *:8443 under /`. Секрет токена печатается один раз и тут же отзывается, в файлы его
класть не нужно.

## 9. Проверка

```bash
D=my-panel.ddns.net
cd /opt/amneziageo-docker
docker compose ps
docker compose logs -n 50
curl -s http://127.0.0.1:8443/api/health      # вариант А
curl --fail --show-error --resolve "$D:8443:127.0.0.1" "https://$D:8443/api/health"  # вариант Б
```

Контейнер в состоянии `running (healthy)`, `health` отвечает и показывает версию панели. Вход в браузере учёткой
из шага 7.

## 10. Первый интерфейс и клиент

В панели:

1. «Подключения» > «Интерфейсы» > добавить: имя (например `awg0`), адрес сети (`10.8.0.1/24`), UDP-порт, адрес,
   по которому клиенты найдут сервер, NAT.
2. «WebSocket прокси» - по желанию. Он слушает TCP-порт сервисов: тот же номер, что UDP-порт интерфейса, если
   другой не задан. В контейнере панель запускает `wstunnel` сама и поднимает его заново, если он упал.
3. «Клиенты» > добавить клиента на этот интерфейс и взять его конфигурацию или QR.

Проверка на сервере:

```bash
cd /opt/amneziageo-docker
ip -d link show type amneziawg
docker compose exec panel amneziageo-server device list
```

## 11. Брандмауэр

На чистом Debian 12 правил нет, порты открыты. Снаружи нужны:

| Порт | Зачем |
|---|---|
| TCP 8443 | панель, только в варианте Б |
| UDP, порт интерфейса | туннель |
| TCP, порт сервисов (по умолчанию тот же номер) | hello, подписки, WebSocket прокси |
| TCP 80 | продление сертификата certbot, вариант Б |

В образе нет ufw, поэтому панель кладёт свои таблицы nftables, а «Открыть порт в брандмауэре» у интерфейса и
«держать порт открытым» у панели и подписок (пункт 23 меню) правила ufw не пишут. Если ufw на хосте включён, его
`drop` сильнее, и порты открываются руками; вкладки «Сервер» и «Подписки» называют такой порт закрытым под полем
порта. Для интерфейса `awg0` на
порту 51820:

```bash
apt install -y ufw
ufw allow OpenSSH
ufw allow 80/tcp       # выпуск и продление Let's Encrypt, вариант Б
ufw allow 8443/tcp
ufw allow 51820/udp
ufw allow 51820/tcp
ufw route allow in on awg0
ufw route allow out on awg0
ufw enable
```

## 12. Обновления

- Кнопкой: в «Обзоре» рядом с версией панели появляется новая и кнопка обновления. С `/var/run/docker.sock` в
  томах панель тянет образ новой версии, копирует базу в `/var/lib/amneziageo-server/backup`, пишет новый тег в
  `.env` и пересоздаёт свой контейнер на новом образе, сохраняя тома, переменные и сеть. Прежний контейнер ждёт
  под своим именем: если новый не станет здоровым за три минуты, база и `.env` возвращаются и снова работает он.
  Интерфейсы и клиенты живут в ядре и через обновление не рвутся.
- Беты: «Настройки» > «Сервер» > «Получать предварительные версии».
- Из консоли: `amneziageo-server update`, бета - `amneziageo-server update beta` (пункты 2 и 3 меню). Скрипт ждёт,
  пока панель ответит новой версией.
- Вручную: копия базы, новый тег в `.env`, перезапуск:

```bash
VER=0.0.5.0
cd /opt/amneziageo-docker
cp -a /var/lib/amneziageo-server /root/amneziageo-db-$(date -u +%Y%m%d-%H%M%S)
printf 'AMNEZIAGEO_TAG=%s\n' "$VER" > .env
docker compose pull
docker compose up -d
docker compose ps
```

- Откат: тот же приём с прежним номером версии; образы предыдущих версий остаются на хосте
  (`docker images | grep amneziageo`). База остаётся такой, какой её оставила новая панель, вернуть её можно
  копией из `/root`, пока контейнер остановлен. `amneziageo-server rollback` (пункт 4 меню) поднимает контейнер
  на ближайшем более старом образе хоста того же имени и предлагает вернуть копию базы из
  `/var/lib/amneziageo-server/backup`.

## Что где лежит

| Путь | Что |
|---|---|
| `/opt/amneziageo-docker/compose.yaml` | описание контейнера; рядом `server.env` и `compose.override.yaml` |
| `/var/lib/amneziageo-server/server.db` | база: учётки, интерфейсы, клиенты, правила |
| `/var/lib/amneziageo-server/geo` | базы Geo, когда они используются |
| `/etc/amneziageo-server/signing.pem` | ключ подписи токенов |
| `/etc/amnezia/amneziawg` | файлы интерфейсов, которые панель читает и пишет |
| `/var/run/docker.sock` | сокет демона, через который панель обновляет себя |
| `/usr/local/bin/amneziageo-server` | меню на хосте, если панель ставилась им; в контейнере то же меню: `docker compose exec panel amneziageo-server` |

## Если не работает

- `amneziageo-server status` (пункт 18 меню): контейнер, ответ панели и чего не хватает хосту.
- `modprobe: FATAL: Module amneziawg not found`: модуль не собран под это ядро. Проверьте заголовки (шаг 2),
  выполните `dkms autoinstall -k $(uname -r)`, смотрите `make.log`.
- `Key was rejected by service` при `modprobe`: включён Secure Boot. Выключите его в настройках ВМ или запишите
  ключ DKMS: `mokutil --import /var/lib/dkms/mok.pub`, перезагрузка, подтверждение в MOK Manager.
- В журнале контейнера `the host carries no amneziawg module`, `forwarding is off in ...` или `the host drops
  forwarded packets`: шаги 3, 4 и 5 соответственно. Журнал: `docker compose logs -n 50`.
- `no administrator yet`: не выполнен шаг 7.
- Контейнер `unhealthy`: панель не отвечает по адресу из `/var/lib/amneziageo-server/health`, причину называет
  журнал контейнера. `AMNEZIAGEO_HEALTH`, оставшийся в `compose.override.yaml` от прежних версий, перебивает этот
  адрес: уберите его.
- Клиент подключился, но интернета нет: пересылка (шаг 4), политика `FORWARD` (шаг 5), NAT у интерфейса.
