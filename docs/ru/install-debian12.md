# Панель AmneziaGeo на Debian 12 без Docker

Панель ставится пакетом из релиза GitHub и работает службой systemd `amneziageo-server` от root. Интерфейсы
AmneziaWG она поднимает сама через ядерный модуль `amneziawg`, поэтому сначала ставится модуль, потом панель. Все
команды выполняются от root.

Нужно: Debian 12 (bookworm) amd64 или arm64, публичный адрес, от 1 ГБ памяти (меньше - временный swap на шаге 3),
около 1 ГБ свободного диска. Docker на этом хосте не нужен: он переводит политику пересылки (FORWARD) в DROP, и
клиенты не выходят дальше сервера.

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
- `nftables` - панель пишет свои таблицы nftables (NAT, закрытые диапазоны, открытые порты).

`ls` должен показать каталог `build`, без него модуль не соберётся.

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

`amneziawg-tools` (`awg`, `awg-quick`) не ставим: панель работает с модулем сама, а программы серии noble собраны
под glibc Ubuntu 24.04 и на Debian 12 могут не встать. Интерфейсы без `awg` показывает консоль панели (шаг 9).

## 4. Пересылка пакетов

```bash
cat > /etc/sysctl.d/90-amneziageo.conf <<'EOF'
net.ipv4.ip_forward = 1
net.ipv6.conf.all.forwarding = 1
EOF
sysctl -p /etc/sysctl.d/90-amneziageo.conf
```

Без пересылки клиенты подключаются, но дальше сервера не ходят. Если IPv6 хоста настраивается автоматически
(SLAAC), добавьте в тот же файл `net.ipv6.conf.<внешний интерфейс>.accept_ra = 2`: с включённой пересылкой ядро
перестаёт принимать объявления маршрутизатора, и хост теряет IPv6-маршрут.

## 5. Пакет панели

Версии: `v0.0.3.0` - последний обычный релиз; `v0.0.5.0-beta.1` - последняя бета (файлы в ней называются
`0.0.5.0`). Для arm64 вместо `x64` поставьте `arm64`.

```bash
TAG=v0.0.3.0
VER=0.0.3.0
ARCH=x64
PKG=amneziageo-server-$VER-linux-$ARCH.tar.gz
cd /root
curl -fLO https://github.com/bor-project/amneziageo_server/releases/download/$TAG/$PKG
curl -fLO https://github.com/bor-project/amneziageo_server/releases/download/$TAG/update.json
want=$(grep -A3 "\"$PKG\"" update.json | sed -n 's/.*"sha256": "\([0-9a-f]*\)".*/\1/p')
echo "$want  $PKG" | sha256sum -c -
```

Последняя команда должна вывести `OK`: хэш пакета совпал с манифестом релиза.

```bash
VER=0.0.3.0
ARCH=x64
PKG=amneziageo-server-$VER-linux-$ARCH.tar.gz
tar xzf /root/$PKG -C /root
/root/amneziageo-server/install.sh
rm -rf /root/amneziageo-server /root/$PKG /root/update.json
```

Что делает `install.sh`:

- кладёт релиз в `/opt/amneziageo-server/releases/<релиз>`, веб-интерфейс в `/opt/amneziageo-server/web/<релиз>` и
  ставит на них ссылки `current` и `wwwroot`;
- создаёт `/var/lib/amneziageo-server` (база) и `/etc/amneziageo-server` с заготовкой `server.env` (все строки
  закомментированы);
- ставит юниты `amneziageo-server.service` и `amneziageo-proxy@.service` (фронты WebSocket прокси) и программу
  `wstunnel` в `/usr/local/bin`;
- включает службу в автозапуск, но не запускает её: сначала нужен администратор.

В конце он пишет `<релиз> is on the host, the server is not running` и две следующие команды.

## 6. Первый администратор

```bash
/opt/amneziageo-server/AmneziaGeo.Server.Cli init --user admin
```

Утилита спросит пароль. С ключом `--generate` она придумает пароль сама и напечатает его один раз
(`password: ...`), сохраните его. `init` работает, только пока в панели нет включённого администратора. Без
`--user` администратором станет учётка хоста, под которой запущена утилита (root), без пароля; для входа через
браузер нужна учётка панели с паролем.

Последней строкой утилита называет порт и путь панели (`the panel answers on port 8443 under /sub/...`). Если
служба ещё ни разу не запускалась, настроек в базе нет и она скажет, что порт и путь назовёт журнал первого
старта.

Пароль выдаётся временным: при первом входе панель попросит его сменить. Ключ `--permanent` оставляет пароль как
есть.

## 7. Где панель отвечает

По умолчанию служба слушает только `127.0.0.1:8443` без TLS, в сети хоста панели не видно. Два варианта.

Адрес и порт панель берёт из `server.env` только до первого запуска: на первом старте она записывает их в свою
базу и дальше читает оттуда. Поэтому вариант Б выбирают до первого `systemctl start`. Если служба уже работала,
адрес меняют в самой панели: «Настройки» > «Сервер» > «Слушать на адресах» > «Все адреса», и `server.env` на
него больше не влияет.

На первом старте панель придумывает себе путь вида `/sub/<16 букв и цифр>/` и пишет его в журнал:

```bash
journalctl -u amneziageo-server | grep "the panel answers"
```

Путь нужен для входа, дальше он виден в «Настройки» > «Сервер» > «Путь». Чтобы панель стояла в корне, до
первого запуска раскомментируйте в `/etc/amneziageo-server/server.env` строку `Web__Path=/`.

**А. Через SSH-туннель, без сертификата**

```bash
systemctl start amneziageo-server
```

На своём компьютере:

```bash
ssh -N -L 8443:127.0.0.1:8443 root@<адрес сервера>
```

и в браузере `http://localhost:8443/sub/<путь из журнала>/`.

**Б. В сети по TLS с сертификатом Let's Encrypt**

### 7.1. Готовое имя хоста и доступность сервера

Предполагаем, что имя уже создано (например, `my-panel.ddns.net` в No-IP) и его A-запись указывает на публичный
IPv4 сервера. Сертификат выпускаем на Debian-сервере для этого имени. Команды ниже выполняются от root в одной
SSH-сессии; замените пример своим именем, без `https://`, порта и пути.

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

### 7.2. Установка Certbot и выпуск сертификата

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

### 7.3. Подключение сертификата к панели

Для новой установки с исходной заготовкой `server.env`:

```bash
D=my-panel.ddns.net
cp -a /etc/amneziageo-server/server.env /etc/amneziageo-server/server.env.before-tls
cat >> /etc/amneziageo-server/server.env <<EOF
Web__Listen__0=*:8443
Web__Certificate=/etc/letsencrypt/live/$D/fullchain.pem
Web__CertificateKey=/etc/letsencrypt/live/$D/privkey.pem
EOF
chmod 600 /etc/amneziageo-server/server.env
systemctl restart amneziageo-server
```

Строки дописываются в конец файла, заготовка остаётся на месте: из одинаковых переменных systemd берёт
последнюю.

Если `server.env` уже содержит ваши настройки, вместо перезаписи измените только три строки `Web__*` из примера,
подставив своё имя в пути сертификата. Остальные параметры сохраните, затем перезапустите службу.

Если служба уже запускалась (вариант А), эти строки сменят только сертификат: адрес и порт останутся теми, что
панель записала при первом старте, и снаружи она не ответит. Зайдите по SSH-туннелю и поставьте «Все адреса» в
«Настройки» > «Сервер». Сертификат из `server.env` тоже действует, только пока свой путь не задан в панели:
её настройки старше конфигурации.

Откройте `https://my-panel.ddns.net:8443/`, подставив своё имя. Проверка с сервера с проверкой доверия и имени:

```bash
curl --fail --show-error --resolve "$D:8443:127.0.0.1" "https://$D:8443/api/health"
```

Панель работает от root и читает ключ с исходными правами. Используйте пути из `live`, не копируйте PEM-файлы
в отдельный каталог: при продлении Certbot обновляет ссылки в `live`. Обновлённый сертификат панель перечитывает
сама, без перезапуска. Адрес, порт и сертификат потом меняются в панели: «Настройки», вкладки «Сервер» и
«Сертификаты».

### 7.4. Автоматическое продление

В пакете Debian есть таймер `certbot.timer`. Включите его и проверьте пробное продление:

```bash
D=my-panel.ddns.net
systemctl enable --now certbot.timer
systemctl list-timers --all certbot.timer
certbot renew --cert-name "$D" --dry-run
```

Пробная проверка должна завершиться успешно; она не заменяет рабочий сертификат. Таймер регулярно запускает
`certbot renew`, а Certbot продлевает сертификаты, которым подошёл срок. Отдельное задание cron не требуется.
Проверить сертификат и журнал автоматических запусков можно так:

```bash
certbot certificates
journalctl -u certbot.service -n 50 --no-pager
```

Для продления имя должно оставаться активным и указывать на сервер, а TCP 80 - оставаться разрешённым извне и
свободным для Certbot. Постоянный веб-сервер на этом порту не нужен: Certbot слушает его только во время проверки.
Если публичный IP меняется, поддерживайте актуальную запись в No-IP; Certbot DNS-записи не обновляет.

Справка: [Certbot: выпуск и продление](https://eff-certbot.readthedocs.io/en/stable/using.html),
[Let's Encrypt: HTTP-01](https://letsencrypt.org/docs/challenge-types/),
[Let's Encrypt: IPv6](https://letsencrypt.org/docs/ipv6-support/).

### 7.5. Панель осталась на `127.0.0.1`

Так выходит, если служба уже запускалась до того, как в `server.env` появился `Web__Listen__0`: адрес панель
берёт из своей базы, а из `server.env` подхватывает только сертификат. В журнале это видно строкой
`the panel answers from 127.0.0.1:8443 under /`, а `ss -ltnp | grep 8443` показывает `127.0.0.1:8443`.

Через SSH-туннель панель при этом открывается, и адрес меняется в ней: «Настройки» > «Сервер» > «Слушать на
адресах» > «Все адреса» > «Сохранить».

То же самое с сервера, без браузера: временный токен, настройки с пустым списком адресов, перезапуск. Нужен
`python3`, в Debian 12 он есть.

```bash
cd /opt/amneziageo-server
api=https://127.0.0.1:8443/api/panel    # без сертификата: http://127.0.0.1:8443/api/panel
out=$(./AmneziaGeo.Server.Cli token add panelfix --role admin --days 1)
id=$(printf '%s\n' "$out" | sed -n 's/^minted \([0-9]*\) as .*/\1/p')
tok=$(printf '%s\n' "$out" | tail -1)
body=$(curl -sk -H "Authorization: Bearer $tok" "$api" | python3 -c 'import json, sys
d = json.load(sys.stdin)
d["listen"] = []
print(json.dumps({k: d[k] for k in ("listen", "domains", "port", "opened", "path", "certificate", "certificateKey", "language", "prereleases")}))')
curl -sk -X PUT -H "Authorization: Bearer $tok" -H "Content-Type: application/json" -d "$body" "$api"
curl -sk -X POST -H "Authorization: Bearer $tok" "$api/restart"
./AmneziaGeo.Server.Cli token revoke "$id" --yes
```

Через несколько секунд `ss -ltnp | grep 8443` показывает `*:8443`, в журнале появляется
`the panel answers from *:8443 under /`, и панель отвечает снаружи. Секрет токена печатается один раз и тут же
отзывается, в файлы его класть не нужно.

## 8. Проверка

```bash
D=my-panel.ddns.net
systemctl status amneziageo-server --no-pager
journalctl -u amneziageo-server -n 50 --no-pager
curl -s http://127.0.0.1:8443/api/health      # вариант А
curl --fail --show-error --resolve "$D:8443:127.0.0.1" "https://$D:8443/api/health"  # вариант Б
```

Служба в состоянии `active (running)`, `health` отвечает и показывает версию панели. Вход в браузере учёткой из
шага 6.

## 9. Первый интерфейс и клиент

В панели:

1. «Подключения» > «Интерфейсы» > добавить: имя (например `awg0`), адрес сети (`10.8.0.1/24`), UDP-порт, адрес,
   по которому клиенты найдут сервер, NAT. Если порты хоста закрыты (ufw), включите «Открыть порт в брандмауэре».
2. «WebSocket прокси» - по желанию. Он слушает TCP-порт сервисов: тот же номер, что UDP-порт интерфейса, если
   другой не задан. Если прокси не запустится, панель снимет галочку и покажет причину.
3. «Клиенты» > добавить клиента на этот интерфейс и взять его конфигурацию или QR.

Проверка на сервере:

```bash
ip -d link show type amneziawg
/opt/amneziageo-server/AmneziaGeo.Server.Cli device list
```

## 10. Брандмауэр

На чистом Debian 12 правил нет, порты открыты. Снаружи нужны:

| Порт | Зачем |
|---|---|
| TCP 8443 | панель, только в варианте Б |
| UDP, порт интерфейса | туннель |
| TCP, порт сервисов (по умолчанию тот же номер) | hello, подписки, WebSocket прокси |
| TCP 80 | продление сертификата certbot, вариант Б |

С ufw панель сама добавляет свои правила, когда включено «Открыть порт в брандмауэре» (у панели, у интерфейса, у
подписок), а включать и выключать ufw не берётся. Руками для интерфейса `awg0` на порту 51820:

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

## 11. Обновления

- Кнопкой: в «Обзоре» рядом с версией панели появляется новая и кнопка обновления. Пакет работает от root под
  systemd, поэтому панель ставит релиз сама: копирует базу, переключает релиз и при сбое возвращает прежний.
  Интерфейсы и клиенты живут в ядре и через обновление не рвутся.
- Беты: «Настройки» > «Сервер» > «Получать предварительные версии».
- Вручную: шаг 5 с новой версией.
- Откат: `/opt/amneziageo-server/current/install.sh --rollback`, список релизов на хосте:
  `/opt/amneziageo-server/current/install.sh --list`. Перед каждым обновлением база копируется в
  `/var/lib/amneziageo-server/backup` (хранятся пять последних копий).

## Что где лежит

| Путь | Что |
|---|---|
| `/opt/amneziageo-server/current` | работающий релиз; рядом `releases`, `web`, `wwwroot`, `previous` |
| `/var/lib/amneziageo-server/server.db` | база: учётки, интерфейсы, клиенты, правила |
| `/var/lib/amneziageo-server/backup` | копии базы перед обновлениями |
| `/etc/amneziageo-server/server.env` | настройки запуска: адрес, сертификат, `Update__*` |
| `/etc/amneziageo-server/signing.pem` | ключ подписи токенов |
| `/etc/systemd/system/amneziageo-server.service` | служба |
| `/usr/local/bin/wstunnel` | фронт WebSocket прокси |

## Если не работает

- `modprobe: FATAL: Module amneziawg not found`: модуль не собран под это ядро. Проверьте заголовки (шаг 2),
  выполните `dkms autoinstall -k $(uname -r)`, смотрите `make.log`.
- `Key was rejected by service` при `modprobe`: включён Secure Boot. Выключите его в настройках ВМ или запишите ключ
  DKMS: `mokutil --import /var/lib/dkms/mok.pub`, перезагрузка, подтверждение в MOK Manager.
- Клиент подключился, но интернета нет: пересылка (шаг 4), NAT у интерфейса, Docker на хосте.
- Служба перезапускается: `journalctl -u amneziageo-server -b`.
