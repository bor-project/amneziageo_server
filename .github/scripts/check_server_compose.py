#!/usr/bin/env python3
"""Checks a Compose installation in a disposable network namespace."""

import argparse
import json
import os
import pathlib
import secrets
import shutil
import subprocess
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--image', required=True)
    parser.add_argument('--sudo', action='store_true')
    parser.add_argument('--bundle', type=pathlib.Path)
    args = parser.parse_args()
    if args.bundle is not None:
        with tempfile.TemporaryDirectory(prefix='ag-compose-bundle-') as temporary:
            source = pathlib.Path(temporary)
            with zipfile.ZipFile(args.bundle) as archive:
                expected = {'compose.yaml', '.env.example', 'server.env.example', 'bootstrap.example.json', 'README.md', 'README.ru.md', 'release.json'}
                if set(archive.namelist()) != expected:
                    raise ValueError('Unexpected installation archive contents')
                for name in sorted(expected):
                    (source / name).write_bytes(archive.read(name))
            verify(args, source)
        return
    verify(args, pathlib.Path(__file__).resolve().parents[2] / 'deploy' / 'compose')


def verify(args, source):
    docker = (['sudo', '-n'] if args.sudo else []) + ['docker']
    checks = []

    def run(command, *, data=None, allowed=False, cwd=None):
        result = subprocess.run(command, input=data, cwd=cwd, text=True, capture_output=True, timeout=180)
        if not allowed and result.returncode:
            raise RuntimeError('command failed: ' + ' '.join(command[:4]) + '\n' + result.stderr[-2000:])
        return result

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        checks.append(name)
        print('PASS ' + name, flush=True)

    for secure in [False, True]:
        with tempfile.TemporaryDirectory(prefix='ag-compose-') as temporary:
            work = pathlib.Path(temporary)
            os.chmod(work, 0o700)
            project = 'ag843-' + secrets.token_hex(6)
            shutil.copy(source / 'compose.yaml', work)
            shutil.copy(source / 'server.env.example', work / 'server.env')
            (work / 'server.env').write_text((work / 'server.env').read_text() + '\nTest__Literal=$literal\'"value\n')
            for name in ['data', 'settings', 'endpoints', 'certificates', 'secrets']:
                (work / name).mkdir(mode=0o700)
            password = 'Compose-' + secrets.token_hex(20) + '$literal'
            (work / 'secrets/admin_password').write_text(password + '\n')
            os.chmod(work / 'secrets/admin_password', 0o600)
            image, tag = args.image.rsplit(':', 1)
            (work / '.env').write_text(f'COMPOSE_PROJECT_NAME={project}\nAMNEZIAGEO_IMAGE={image}\nAMNEZIAGEO_TAG={tag}\n')
            setup = json.loads((source / 'bootstrap.example.json').read_text())
            setup['user'] = 'compose-admin'
            setup['changePassword'] = False
            setup['routingDefaults'] = False
            port = 19444 if secure else 19443
            scheme = 'https' if secure else 'http'
            setup['panel'].update(port=port, path='compose-check', language='ru')
            if secure:
                folder = work / 'certificates' / 'test'
                folder.mkdir()
                run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                     '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1',
                     '-keyout', str(folder / 'privkey.pem'), '-out', str(folder / 'fullchain.pem')])
                setup['panel'].update(certificate='/etc/letsencrypt/test/fullchain.pem', certificateKey='/etc/letsencrypt/test/privkey.pem')
                setup['validation'] = {'trustedCertificates': ['/etc/letsencrypt/test/fullchain.pem']}
            (work / 'bootstrap.json').write_text(json.dumps(setup))
            os.chmod(work / 'bootstrap.json', 0o600)
            compose = docker + ['compose', '--project-directory', str(work), '-f', str(work / 'compose.yaml')]
            if args.bundle is not None:
                original = (work / '.env').read_text()
                (work / '.env').write_text((source / '.env.example').read_text())
                rendered = json.loads(run(compose + ['config', '--format', 'json']).stdout)
                release = json.loads((source / 'release.json').read_text())
                check(rendered['services']['panel']['image'] == release['image'], 'archive selects the release image')
                (work / '.env').write_text(original)
            default = json.loads(run(compose + ['config', '--format', 'json']).stdout)
            check(default['services']['panel']['network_mode'] == 'host', 'production host network rendered')
            check('NET_ADMIN' in default['services']['panel']['cap_add'], 'production capability rendered')
            check('build' not in default['services']['panel'], 'installation uses an image without source build')
            check(password not in json.dumps(default), 'password absent from Compose config')
            env_text = (work / '.env').read_text()
            (work / '.env').write_text(env_text.replace('AMNEZIAGEO_TAG=' + tag, 'AMNEZIAGEO_TAG='))
            check(run(compose + ['config', '--quiet'], allowed=True).returncode != 0, 'missing image version rejected')
            (work / '.env').write_text(env_text)
            (work / 'isolated.yaml').write_text('''services:
  panel:
    network_mode: none
    restart: "no"
    volumes: !override
      - ./data:/var/lib/amneziageo-server
      - ./settings:/etc/amneziageo-server
      - ./endpoints:/etc/amnezia/amneziawg
      - ./certificates:/etc/letsencrypt:ro
''')
            compose += ['-f', str(work / 'isolated.yaml')]

            def execute(words, **kwargs):
                return run(compose + ['exec', '-T', 'panel'] + words, **kwargs)

            base = f'{scheme}://127.0.0.1:{port}/compose-check'
            tls = ['--cacert', '/etc/letsencrypt/test/fullchain.pem'] if secure else []

            def curl(path, body=None):
                words = ['curl', '-fsS'] + tls + [base + path]
                if body is not None:
                    words += ['-H', 'Content-Type: application/json', '--data-binary', '@-']
                return execute(words, data=None if body is None else json.dumps(body)).stdout

            try:
                run(compose + ['up', '-d', '--pull', 'never', '--wait', '--wait-timeout', '120'])
                identity = run(compose + ['ps', '-q', 'panel']).stdout.strip()
                inspect = json.loads(run(docker + ['inspect', identity]).stdout)[0]
                check(inspect['HostConfig']['NetworkMode'] == 'none', 'acceptance network isolated')
                check('Test__Literal=$literal\'"value' in inspect['Config']['Env'], 'runtime env preserves literal dollar and quotes')
                check(password not in json.dumps(inspect['Config']), 'password absent from container environment and command')
                check(json.loads(curl('/api/health'))['status'] == 'ok', scheme + ' health at custom path and port')
                login = json.loads(curl('/api/auth/login', {'user': 'compose-admin', 'password': password}))
                check(bool(login.get('access')), scheme + ' administrator password accepted')
                html = curl('/')
                check('<html' in html.lower(), scheme + ' panel page served')
                key = execute(['sha256sum', '/etc/amneziageo-server/signing.pem']).stdout.split()[0]
                execute(['amneziageo-server', 'panel', 'set', '--path', 'changed-by-admin'])
                run(compose + ['up', '-d', '--pull', 'never', '--force-recreate', '--wait', '--wait-timeout', '120'])
                check(execute(['amneziageo-server', 'panel', 'get', 'path']).stdout.strip() == '/changed-by-admin/', 'recreation preserves administrative changes')
                check(execute(['sha256sum', '/etc/amneziageo-server/signing.pem']).stdout.split()[0] == key, 'recreation preserves signing key')
                base = f'{scheme}://127.0.0.1:{port}/changed-by-admin'
                check(bool(json.loads(curl('/api/auth/login', {'user': 'compose-admin', 'password': password})).get('access')), 'recreation preserves administrator password')
                logs = run(compose + ['logs', '--no-color', 'panel']).stdout
                check(password not in logs, 'logs do not contain password')
                for binary, words in [('wstunnel', ['--version']), ('wstunnel-get', ['get-server', '--help']), ('wstunnel-quic', ['server', '--help'])]:
                    execute(['/opt/amneziageo-server/' + binary] + words)
                    check(True, binary + ' available inside image')
                check('bootstrap: existing administrator and settings retained' in logs, 'restart skips initialization')
                invalid = dict(setup, panel=dict(setup['panel'], port=0))
                execute(['/bin/sh', '-c', 'cat > /run/invalid.json'], data=json.dumps(invalid))
                refused = execute(['/opt/amneziageo-server/AmneziaGeo.Server.Cli', 'bootstrap', '--db', '/var/lib/amneziageo-server/invalid.db', '--file', '/run/invalid.json'], allowed=True)
                check(refused.returncode == 78 and 'panel.port:' in refused.stderr, 'invalid initial port reports field validation')
                check(password not in refused.stdout + refused.stderr, 'validation does not disclose password')
                def reject(configuration, field, label, environment=None):
                    execute(['/bin/sh', '-c', 'cat > /run/invalid.json'], data=json.dumps(configuration))
                    words = ['/opt/amneziageo-server/AmneziaGeo.Server.Cli', 'bootstrap', '--db', '/var/lib/amneziageo-server/invalid.db', '--file', '/run/invalid.json']
                    if environment:
                        words = ['env', environment] + words
                    answer = execute(words, allowed=True)
                    check(answer.returncode == 78 and field in answer.stderr, label)
                    check(password not in answer.stdout + answer.stderr, label + ' protects secret')

                reject(dict(setup, panel=dict(setup['panel'], port=port)), 'panel.port:', 'active listener blocks initialization')
                reject(dict(setup, panel=dict(setup['panel'], listen=['203.0.113.199'], port=19555)), 'panel.port:', 'foreign listen IP blocks initialization')
                reject(dict(setup, panel=dict(setup['panel'], domains=['localhost'], port=19555),
                            validation=dict(setup.get('validation', {}), expectedAddresses={'localhost': ['203.0.113.199']})), 'panel.domains:', 'wrong DNS target blocks initialization')
                reject(dict(setup, passwordFile='/run/secrets/missing'), 'passwordFile:', 'missing password file blocks initialization')
                reject(dict(setup, panel=dict(setup['panel'], port=19555)), 'runtime.Proxy__Enabled:', 'invalid runtime boolean blocks initialization', 'Proxy__Enabled=invalid')
                if secure:
                    reject(dict(setup, panel=dict(setup['panel'], domains=['127.0.0.2'], listen=['127.0.0.2'], port=19555)), 'panel.certificate:', 'wrong certificate SAN blocks initialization')
                    reject(dict(setup, validation={}), 'panel.certificate:', 'untrusted certificate blocks initialization')
                valid = dict(setup, panel=dict(setup['panel'], port=19555))
                execute(['/bin/sh', '-c', 'cat > /run/invalid.json'], data=json.dumps(valid))
                checked = execute(['/opt/amneziageo-server/AmneziaGeo.Server.Cli', 'bootstrap', '--check', '--db', '/var/lib/amneziageo-server/invalid.db', '--file', '/run/invalid.json'])
                check('initial settings were not committed' in checked.stdout, 'dry-run succeeds after all rejected attempts')
                accounts = execute(['/opt/amneziageo-server/AmneziaGeo.Server.Cli', 'user', 'list', '--db', '/var/lib/amneziageo-server/invalid.db']).stdout
                check('compose-admin' not in accounts, 'failed initialization and dry-run create no administrator')
            except Exception:
                logs = run(compose + ['logs', '--no-color', 'panel'], allowed=True).stdout
                print(logs.replace(password, '<redacted>')[-4000:], flush=True)
                raise
            finally:
                run(compose + ['down', '--remove-orphans'], allowed=True)
                run(docker + ['run', '--rm', '--network', 'none', '-v', str(work) + ':/cleanup', '--entrypoint', '/bin/sh', args.image,
                              '-c', 'rm -rf /cleanup/data /cleanup/settings /cleanup/endpoints'], allowed=True)
    print(json.dumps({'passed': len(checks), 'failed': 0, 'image': args.image}))


if __name__ == '__main__':
    main()
