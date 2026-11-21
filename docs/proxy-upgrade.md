# Proxy service upgrades

The package installer transfers legacy `amneziageo-proxy@*.service` instances to the independent `amneziageo-proxy.service`. Existing proxy paths, restrictions, credentials and client configurations remain in place. WebSocket connections reconnect during the service handover.

Before changing ownership, the installer records the proxy flags, legacy service states and independent service deployment. It stops the panel before stopping legacy instances, disables those instances, installs the daemon and starts the updated panel. A failed update restores the preceding release and service ownership. If conflicting services cannot be stopped during recovery, recovery reports the error and leaves the panel stopped.

Running the same package again also checks service ownership. If the panel is stopped while a legacy proxy remains active, installation stops before interrupting that proxy; start the panel before retrying the migration.

## Rollback

Use the server menu to roll back. The menu uses the retained handover-aware installer when available. The direct equivalent is:

```sh
sudo bash /var/lib/amneziageo-server/proxy-handover/installer/install.sh --rollback
```

The retained installer also handles returning from the legacy release to the newer release. Use it rather than an installer bundled with an older release. Ownership snapshots are stored under `/var/lib/amneziageo-server/proxy-handover/releases` with restricted directory permissions. They contain service states, the `Proxy__Enabled`/`Proxy__Isolated` flags and the `proxy-*.env`/`proxy-*.yaml` files with their ownership and permissions; they do not copy the complete panel environment. The installer refreshes these file copies after stopping the panel and restores them before starting legacy fronts. Files absent from a snapshot are removed during recovery.

Snapshots created by older installers remain readable, but they contain no proxy file copies. They cannot recover legacy files already deleted by the newer panel. If a required legacy environment file is missing from such a snapshot and the host, rollback refuses before stopping the running panel; restore a pre-upgrade configuration backup first.

Rollback keeps the database, following the existing package rollback behavior. Failed installation restores the database backup taken before switching releases. Do not manually enable a legacy instance while the new daemon owns its listener.

## Process failures

The daemon retains recognized process failure codes from a bounded stderr buffer. The journal and panel receive fixed descriptions such as an occupied listener address, permission refusal or an invalid configuration. Raw stderr, command arguments, credentials and keys are not forwarded in failure messages. Unknown output retains the process exit code.

The control protocol keeps the numeric exit-code line. New implementations may append a recognized failure code; older implementations remain compatible. Reading output after process exit has a deadline, including when a descendant retains an inherited pipe.

## Installer regression checks

Run the installer tests with:

```sh
python3 -m unittest discover -s deploy/tests -v
```

The integration check uses an existing image with systemd, Python 3 and the usual server runtime dependencies. It creates and removes its own privileged Docker containers with `--network none`, mounts the supplied packages read-only and never installs packages on the host:

```sh
python3 deploy/tests/check_proxy_handover_systemd.py /path/to/old-package /path/to/new-package \
  --image local-systemd:24.04 --output /path/to/results
```

Use the official `v1.0.0.0` package as the old package. The check covers a clean installation without legacy units, reinstallation, interrupted updates, two rollback/return cycles and older snapshots missing configuration files. Both packaged APIs and systemd run normally. A synthetic interface record and a loopback UDP echo backend exercise WebSocket forwarding without loading a VPN kernel module. This check does not exercise an AmneziaWG handshake or a client connection through the public TLS endpoint.
