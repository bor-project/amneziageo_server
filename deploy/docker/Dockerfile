# syntax=docker/dockerfile:1
# check=skip=SecretsUsedInArgOrEnv

# The panel, its console and the websocket tool, built from the repository root for amd64 and arm64:
#   docker buildx build -f deploy/docker/Dockerfile --platform linux/amd64,linux/arm64 -t amneziageo-server:<version> .

ARG DOTNET=10.0

FROM --platform=$BUILDPLATFORM node:22-trixie-slim AS web
WORKDIR /src
COPY amneziageo-web/package.json amneziageo-web/package-lock.json amneziageo-web/
RUN npm --prefix amneziageo-web ci --no-audit --no-fund
COPY amneziageo-web/ amneziageo-web/
RUN mkdir -p amneziageo-server/AmneziaGeo.Server.Api \
 && npm --prefix amneziageo-web run build

FROM rust:1.95-bookworm AS tunnel
RUN apt-get update \
 && apt-get install -y --no-install-recommends cmake libclang-dev \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY wstunnel/wstunnel/ ./
RUN cargo build --release --package wstunnel-cli \
 && cp target/release/wstunnel /wstunnel

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:$DOTNET AS server
ARG TARGETARCH
ARG VERSION=0.0.1
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY amneziageo-server/ amneziageo-server/
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "no build for $TARGETARCH" >&2; exit 1 ;; \
    esac \
 && for project in Api Cli; do \
      dotnet publish "amneziageo-server/AmneziaGeo.Server.$project" -c Release -r "$rid" --self-contained false \
        -p:Version="$VERSION" -o /out --nologo -v minimal || exit 1; \
    done \
 && rm -rf /out/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:$DOTNET
ARG VERSION=0.0.1
LABEL org.opencontainers.image.source=https://github.com/bor-project/amneziageo_server \
      org.opencontainers.image.version=$VERSION
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl docker-compose-v2 iproute2 nftables tini tzdata \
 && rm -rf /var/lib/apt/lists/* \
 && ln -s /usr/libexec/docker/cli-plugins/docker-compose /usr/local/bin/docker-compose
COPY --from=server /out/ /opt/amneziageo-server/
COPY --from=web /src/amneziageo-server/AmneziaGeo.Server.Api/wwwroot/ /opt/amneziageo-server/wwwroot/
COPY --from=tunnel /wstunnel /usr/local/bin/wstunnel
COPY --chmod=755 deploy/docker/entrypoint.sh /usr/local/bin/amneziageo-server-entrypoint
COPY --chmod=755 deploy/docker/update.sh /usr/local/bin/amneziageo-server-update
RUN ln -s /opt/amneziageo-server/AmneziaGeo.Server.Cli /usr/local/bin/amneziageo-server
ENV DOTNET_NOLOGO=1 \
    AMNEZIAGEO_CONTAINER=1 \
    ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_WEBROOT=/opt/amneziageo-server/wwwroot \
    AMNEZIAGEO_DB=/var/lib/amneziageo-server/server.db \
    AMNEZIAGEO_SIGNING_KEY=/etc/amneziageo-server/signing.pem \
    Web__Listen__0=127.0.0.1:8443 \
    Endpoints__Directory=/etc/amnezia/amneziawg \
    Geo__Path=/var/lib/amneziageo-server/geo \
    Auth__HostLogin=Off \
    Auth__HostUsers=false
WORKDIR /opt/amneziageo-server
VOLUME ["/var/lib/amneziageo-server", "/etc/amneziageo-server"]
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s \
  CMD curl -fsSk "${AMNEZIAGEO_HEALTH:-http://127.0.0.1:8443/api/health}" > /dev/null || exit 1
ENTRYPOINT ["tini", "--", "amneziageo-server-entrypoint"]
