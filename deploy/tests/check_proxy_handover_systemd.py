#!/usr/bin/env python3
"""Checks package handover with systemd and the packaged APIs in isolated containers."""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import socket
import sqlite3
import subprocess
import sys
import time
import uuid


def run(*args, check=True, timeout=120):
    result = subprocess.run(args, text=True, capture_output=True, timeout=timeout)
    if check and result.returncode:
        raise RuntimeError(f'{args[0]} failed ({result.returncode}): {result.stdout[-2000:]} {result.stderr[-2000:]}')
    return result


def api(method, path, body=None):
    journal = run('journalctl', '-u', 'amneziageo-server', '--no-pager').stdout
    prefix = re.findall(r'the panel answers from .*? under /(\w+)/', journal)[-1]
    login = run('/opt/amneziageo-server/current/AmneziaGeo.Server.Cli', 'login').stdout
    token = next(line.split(':', 1)[1].strip() for line in login.splitlines() if line.startswith('access:'))
    connection = http.client.HTTPConnection('127.0.0.1', 8443, timeout=30)
    connection.request(method, f'/{prefix}/api/{path}', None if body is None else json.dumps(body),
                       {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'})
    response = connection.getresponse()
    payload = response.read()
    connection.close()
    if response.status >= 300:
        fault = json.loads(payload)
        raise RuntimeError(f'API {method} {path}: {response.status}, {fault.get("code")}, {fault.get("message")}')
    return json.loads(payload)


def inside(action):
    if action == 'health':
        connection = http.client.HTTPConnection('127.0.0.1', 8443, timeout=10)
        connection.request('GET', '/api/health')
        response = connection.getresponse()
        assert response.status == 200, response.status
        response.read()
        print('HTTP 200')
    elif action == 'configure':
        configs = api('GET', 'configs')
        config = configs[0] if configs else api('GET', 'configs/draft?name=awg0')
        config.update(isEnabled=False, webSocket=True, servicesPort=8080, host='127.0.0.1')
        config = api('PUT', f"configs/{config['id']}", config) if configs else api('POST', 'configs', config)
        Path('/state/config.json').write_text(json.dumps(config))
        print('created legacy WebSocket configuration using the real API')
    elif action == 'activate-fixture':
        with sqlite3.connect('/var/lib/amneziageo-server/server.db') as database:
            database.execute('UPDATE Configs SET IsEnabled=1 WHERE Name=?', ('awg0',))
        print('enabled the synthetic fixture in the stopped database')
    elif action == 'kill-updated-panel':
        release = Path('/new/release').read_text().strip()
        for _ in range(300):
            current = Path('/opt/amneziageo-server/current')
            if current.is_symlink() and os.readlink(current) == 'releases/' + release:
                if not Path('/etc/amneziageo-server/proxy-awg0.env').exists():
                    pid = int(run('systemctl', 'show', '-p', 'MainPID', '--value', 'amneziageo-server').stdout)
                    if pid > 0:
                        os.kill(pid, 9)
                        print('terminated the updated API after legacy file withdrawal')
                        return
            time.sleep(.1)
        raise RuntimeError('the updated API did not withdraw the legacy files')
    elif action == 'remove-file-backup':
        release = Path('/old/release').read_text().strip()
        assert re.fullmatch(r'[a-zA-Z0-9._-]+', release)
        shutil.rmtree(Path('/var/lib/amneziageo-server/proxy-handover/releases') / release / 'fronts')
    elif action == 'files':
        files = {}
        for suffix in ('env', 'yaml'):
            path = Path('/etc/amneziageo-server/proxy-awg0.' + suffix)
            if path.exists():
                stat = path.stat()
                files[path.name] = {'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                                    'mode': stat.st_mode & 0o777, 'uid': stat.st_uid, 'gid': stat.st_gid}
        print(json.dumps(files))
    elif action == 'echo':
        server = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        config = json.loads(Path('/state/config.json').read_text())
        server.bind(('127.0.0.1', config['listenPort']))
        while True:
            payload, address = server.recvfrom(65535)
            server.sendto(payload, address)
    elif action == 'probe':
        client = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        client.settimeout(15)
        for index in range(8):
            payload = os.urandom(37 + 137 * index)
            client.sendto(payload, ('127.0.0.1', 51891))
            assert client.recvfrom(65535)[0] == payload
        print('8 exact bidirectional UDP datagrams through WebSocket')
    elif action == 'start-client':
        config = json.loads(Path('/state/config.json').read_text())
        arguments = Path('/etc/amneziageo-server/proxy-awg0.env').read_text()
        endpoint = re.search(r'ws://[^\s"\']+', arguments)[0]
        run('systemd-run', '--unit=ag805-client', '/opt/amneziageo-server/current/wstunnel', 'client',
            '--http-upgrade-path-prefix', config['webSocketPath'], '-L',
            f"udp://51891:127.0.0.1:{config['listenPort']}", endpoint)
    else:
        raise ValueError(action)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('old_package', type=Path)
    parser.add_argument('new_package', type=Path)
    parser.add_argument('--image', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    container = 'ag805-handover-' + uuid.uuid4().hex[:12]
    report = {'container': container, 'old_release': (args.old_package / 'release').read_text().strip(),
              'new_release': (args.new_package / 'release').read_text().strip(), 'checks': []}

    def docker(*words, **kwargs):
        return run('sudo', '-n', 'docker', *words, **kwargs)

    def execute(*words, **kwargs):
        return docker('exec', container, *words, **kwargs)

    def check(label, *words):
        result = execute(*words)
        report['checks'].append({'name': label, 'result': 'PASS', 'output': result.stdout.strip()[-1000:]})
        print(label + ': PASS', flush=True)
        return result.stdout.strip()

    def files():
        return json.loads(execute('python3', '/check.py', '--inside', 'files').stdout)

    def probe(label):
        execute('python3', '/check.py', '--inside', 'start-client')
        try:
            check(label, 'python3', '/check.py', '--inside', 'probe')
        finally:
            execute('systemctl', 'stop', 'ag805-client')
            execute('systemctl', 'reset-failed', 'ag805-client', check=False)

    def start():
        docker('run', '-d', '--name', container, '--privileged', '--network', 'none', '--cgroupns=host',
               '-v', f'{args.old_package.resolve()}:/old:ro', '-v', f'{args.new_package.resolve()}:/new:ro',
               '-v', f'{Path(__file__).resolve()}:/check.py:ro', '-v', '/lib/modules:/lib/modules:ro',
               args.image, '/sbin/init')
        for _ in range(30):
            if execute('systemctl', 'show-environment', check=False).returncode == 0:
                break
            time.sleep(1)
        report['systemd'] = execute('systemctl', '--version').stdout.splitlines()[0]
        execute('mkdir', '-p', '/state')

    try:
        start()
        assert execute('systemctl', 'list-unit-files', '--no-legend', 'amneziageo-proxy@*.service', check=False).returncode == 1
        check('clean install without legacy template', 'bash', '/new/install.sh', '--with-proxy')
        execute('systemctl', 'start', 'amneziageo-server')
        check('clean API health', 'python3', '/check.py', '--inside', 'health')
        check('clean daemon active', 'systemctl', 'is-active', 'amneziageo-proxy.service')
        check('clean reinstall', 'bash', '/new/install.sh', '--with-proxy')
        docker('rm', '-f', container)
        start()
        check('install old official package', 'bash', '/old/install.sh')
        execute('systemctl', 'start', 'amneziageo-server')
        check('old API health', 'python3', '/check.py', '--inside', 'health')
        check('configure legacy WebSocket', 'python3', '/check.py', '--inside', 'configure')
        execute('systemctl', 'stop', 'amneziageo-server')
        check('activate UDP echo fixture', 'python3', '/check.py', '--inside', 'activate-fixture')
        execute('systemctl', 'start', 'amneziageo-server')
        time.sleep(2)
        check('legacy active before upgrade', 'systemctl', 'is-active', 'amneziageo-proxy@awg0.service')
        original_files = files()
        assert len(original_files) == 2, original_files
        report['original_files'] = original_files
        execute('systemd-run', '--unit=ag805-echo', 'python3', '/check.py', '--inside', 'echo')
        probe('legacy exchange before upgrade')
        execute('systemd-run', '--unit=ag805-break', 'python3', '/check.py', '--inside', 'kill-updated-panel')
        broken = execute('bash', '/new/install.sh', '--with-proxy', check=False)
        assert broken.returncode != 0, 'the interrupted API update unexpectedly succeeded'
        check('old API health after interrupted update', 'python3', '/check.py', '--inside', 'health')
        assert files() == original_files, 'automatic recovery did not restore legacy files'
        probe('legacy exchange after automatic recovery')
        check('upgrade package', 'bash', '/new/install.sh', '--with-proxy')
        check('new API health', 'python3', '/check.py', '--inside', 'health')
        check('independent daemon active', 'systemctl', 'is-active', 'amneziageo-proxy.service')
        assert execute('systemctl', 'is-active', '--quiet', 'amneziageo-proxy@awg0.service', check=False).returncode != 0
        for _ in range(30):
            if not files():
                break
            time.sleep(1)
        assert files() == {}, 'the actual API did not withdraw the legacy files'
        report['checks'].append({'name': 'actual API deleted legacy env and YAML', 'result': 'PASS'})
        for cycle in range(2):
            check(f'reinstall {cycle + 1}', 'bash', '/new/install.sh', '--with-proxy')
            check(f'rollback {cycle + 1}', 'bash', '/var/lib/amneziageo-server/proxy-handover/installer/install.sh', '--rollback')
            check(f'old API health after rollback {cycle + 1}', 'python3', '/check.py', '--inside', 'health')
            assert files() == original_files, 'legacy configuration metadata or content changed'
            check(f'legacy active after rollback {cycle + 1}', 'systemctl', 'is-active', 'amneziageo-proxy@awg0.service')
            probe(f'legacy exchange after rollback {cycle + 1}')
            check(f'return to new release {cycle + 1}', 'bash', '/var/lib/amneziageo-server/proxy-handover/installer/install.sh', '--rollback')
            check(f'new API health after return {cycle + 1}', 'python3', '/check.py', '--inside', 'health')
            assert files() == {}, 'new API retained legacy configuration'
        execute('python3', '/check.py', '--inside', 'remove-file-backup')
        pid = execute('systemctl', 'show', '-p', 'MainPID', '--value', 'amneziageo-server').stdout
        refused = execute('bash', '/var/lib/amneziageo-server/proxy-handover/installer/install.sh', '--rollback', check=False)
        assert refused.returncode != 0 and 'configuration backup is missing' in refused.stderr
        assert execute('systemctl', 'show', '-p', 'MainPID', '--value', 'amneziageo-server').stdout == pid
        check('old snapshot without files refuses without interrupting the API', 'python3', '/check.py', '--inside', 'health')
        report['result'] = 'PASS'
    except Exception as error:
        report['result'] = 'FAIL'
        report['error'] = str(error)
        raise
    finally:
        (args.output / 'systemd-result.json').write_text(json.dumps(report, indent=2))
        docker('rm', '-f', container, check=False)


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--inside':
        inside(sys.argv[2])
    else:
        main()
