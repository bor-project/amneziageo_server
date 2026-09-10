#!/usr/bin/env bash
# Puts the panel of this directory on the host and readies its service.
set -e

if [ "$(id -u)" != 0 ]; then
  echo "run this as root" >&2
  exit 1
fi

here=$(cd "$(dirname "$0")" && pwd)

install -d /opt/amneziageo-server /var/lib/amneziageo-server /etc/amneziageo-server
systemctl stop amneziageo-server 2>/dev/null || true

cp -a "$here/publish/." /opt/amneziageo-server/
chmod +x /opt/amneziageo-server/AmneziaGeo.Server.Api /opt/amneziageo-server/AmneziaGeo.Server.Cli

if [ ! -f /etc/amneziageo-server/server.env ]; then
  cat > /etc/amneziageo-server/server.env <<'ENV'
# Web__Listen__0=*:8443
# Web__Certificate=/etc/letsencrypt/live/example.org/fullchain.pem
# Web__CertificateKey=/etc/letsencrypt/live/example.org/privkey.pem
ENV
  chmod 600 /etc/amneziageo-server/server.env
fi

install -m 644 "$here/amneziageo-server.service" /etc/systemd/system/amneziageo-server.service
install -m 755 "$here/wstunnel" /usr/local/bin/wstunnel
install -m 644 "$here/amneziageo-proxy@.service" /etc/systemd/system/amneziageo-proxy@.service
install -m 644 "$here/amneziageo-relay@.service" /etc/systemd/system/amneziageo-relay@.service
systemctl daemon-reload
systemctl enable amneziageo-server

cat <<'EOF'
the panel is on the host.

  /opt/amneziageo-server/AmneziaGeo.Server.Cli init      make the first administrator
  systemctl start amneziageo-server                      start it
  ssh -N -L 8443:127.0.0.1:8443 <this host>              reach http://localhost:8443
EOF
