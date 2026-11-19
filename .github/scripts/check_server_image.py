import argparse
import hashlib
import http.client
import json
import pathlib
import re
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import uuid


NATIVE = '/opt/amneziageo-server/'
ROOT = '/tmp/image-check'


def run(*args, input=None, timeout=90):
    result = subprocess.run(args, input=input, capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('command failed: ' + ' '.join(args)[:400] + '; exit=' + str(result.returncode) + '; ' + result.stderr[-1000:])
    return result.stdout


def request_inside():
    value = json.load(sys.stdin)
    context = ssl.create_default_context(cafile=ROOT + '/ca.crt')
    connection = http.client.HTTPSConnection('127.0.0.1', 443, context=context, timeout=30)
    connection.request(value['method'], '/image-check/api/' + value['path'],
                       None if value.get('body') is None else json.dumps(value['body']),
                       headers=value.get('headers', {}) | {'Content-Type': 'application/json'})
    response = connection.getresponse()
    raw = response.read()
    print(json.dumps({'status': response.status, 'body': json.loads(raw) if raw else None}))


def echo_inside():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as peer:
        peer.bind(('0.0.0.0', 42000))
        while True:
            packet, address = peer.recvfrom(65536)
            peer.sendto(packet, address)


def probe_inside(port, count):
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as peer:
        peer.settimeout(1)
        received = 0
        stale_warm_replies = 0
        for attempt in range(10 if count > 1 else 2):
            packet = b'ag797-ready'
            peer.sendto(packet, ('127.0.0.1', port))
            try:
                if peer.recv(65536) == packet:
                    break
            except TimeoutError:
                pass
        else:
            print(json.dumps({'sent': 0, 'received': 0}))
            return
        for index in range(count):
            packet = index.to_bytes(4, 'big') + bytes(1096)
            peer.sendto(packet, ('127.0.0.1', port))
            deadline = time.monotonic() + 1
            while time.monotonic() < deadline:
                peer.settimeout(max(0.001, deadline - time.monotonic()))
                try:
                    answer = peer.recv(65536)
                except TimeoutError:
                    break
                if answer == b'ag797-ready':
                    stale_warm_replies += 1
                    continue
                if answer != packet:
                    raise RuntimeError('unexpected UDP echo payload')
                received += 1
                break
        print(json.dumps({'sent': count, 'received': received, 'staleWarmReplies': stale_warm_replies}))


def verify(image, fixture_image, isolated, wg_driver=None, previous_image=None):
    label = 'ag-image-' + uuid.uuid4().hex[:12]
    fixture = label + '-fixture'
    server = label + '-server'
    owned = []
    token = ''

    def check(name, passed, **values):
        print(json.dumps({'check': name, 'pass': bool(passed), **values}), flush=True)
        if not passed:
            raise RuntimeError(name)

    def api(method, path, body=None):
        text = run('docker', 'exec', '-i', fixture, 'python', ROOT + '/check.py', '--request',
                   input=json.dumps({'method': method, 'path': path, 'body': body,
                                     'headers': {'Authorization': 'Bearer ' + token}}))
        return json.loads(text)

    def success(method, path, body=None):
        value = api(method, path, body)
        detail = value['body'] if isinstance(value['body'], dict) else {}
        check(method + ' ' + path, 200 <= value['status'] < 300, status=value['status'],
              error=detail.get('error'), field=detail.get('field'))
        return value['body']

    def create_server(selected, volumes=None):
        args = ['docker', 'run', '-d', '--name', server, '--network', 'container:' + fixture, '--cap-add', 'NET_ADMIN',
                '--memory', '512m', '--cpus', '1', '--label', 'amneziageo.image-check=' + label,
                '-e', 'Web__Listen__0=0.0.0.0:443', '-e', 'Web__Certificate=' + ROOT + '/server.crt',
                '-e', 'Web__CertificateKey=' + ROOT + '/server.key', '-e', 'Proxy__Isolated=' + str(isolated).lower()]
        if volumes:
            args += ['--volumes-from', volumes]
        run(*args, '--entrypoint', 'sleep', selected, 'infinity')
        run('docker', 'cp', str(directory), server + ':' + ROOT)

    def start_services():
        if isolated:
            run('docker', 'exec', '-d', server, NATIVE + 'AmneziaGeo.Server.Proxy.Host', 'serve',
                '--config', ROOT + '/catalog.json', '--state', ROOT + '/proxy-state')
            for attempt in range(120):
                ready = subprocess.run(['docker', 'exec', server, 'test', '-S', '/run/amneziageo-proxy/control.sock'], capture_output=True)
                if ready.returncode == 0:
                    break
                time.sleep(0.25)
            else:
                raise RuntimeError('proxy daemon did not start')
        run('docker', 'exec', '-d', server, '/bin/sh', '-c', 'exec "$@" > /tmp/image-check/api.log 2>&1',
            'sh', NATIVE + 'AmneziaGeo.Server.Api')
        last_error = ''
        for attempt in range(60):
            try:
                if api('GET', 'health')['status'] == 200:
                    return
            except (RuntimeError, json.JSONDecodeError) as error:
                last_error = str(error)
            time.sleep(0.5)
        logs = run('docker', 'exec', server, 'tail', '-n', '15', ROOT + '/api.log')
        raise RuntimeError('panel did not become healthy; ' + last_error[-1000:] + '; ' + logs)

    def client(path, credential, port, directory, target=42000, websocket=False):
        headers = directory / ('headers-' + str(port))
        headers.write_text('Authorization: Bearer ' + credential + '\n' +
                           ('X-AmneziaGeo-Target: 127.0.0.1:' + str(target) + '\n' if websocket else ''))
        headers.chmod(0o600)
        run('docker', 'cp', str(headers), server + ':' + ROOT + '/' + headers.name)
        run('docker', 'exec', '-d', '-e', 'SSL_CERT_FILE=' + ROOT + '/ca.crt',
            '-e', 'CLIENT_LOG=' + ROOT + '/client-' + str(port) + '.log', server,
            '/bin/sh', '-c', 'exec "$@" > "$CLIENT_LOG" 2>&1', 'sh',
            '/usr/local/bin/wstunnel' if websocket else NATIVE + 'wstunnel-quic', 'client', '--tls-verify-certificate',
            '--http-headers-file', ROOT + '/' + headers.name, '-P', path,
            '-L', 'udp://127.0.0.1:' + str(port) + ':127.0.0.1:' + str(target),
            ('wss' if websocket else 'wts') + '://127.0.0.1:443')

    def probe(port, count=32):
        measured = json.loads(run('docker', 'exec', fixture, 'python', ROOT + '/check.py', '--probe', str(port), '--count', str(count), timeout=40))
        if count > 1 and measured['received'] == 0:
            logs = run('docker', 'exec', server, 'tail', '-n', '12', ROOT + '/client-' + str(port) + '.log')
            print(re.sub(r'(?i)(Bearer )[^\s\"\']+', r'\1[redacted]', logs), flush=True)
        return measured

    def refused(port):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            logs = run('docker', 'exec', server, 'tail', '-n', '20', ROOT + '/client-' + str(port) + '.log')
            if re.search(r'(?i)\b(?:403|404|forbidden|unauthorized|rejected)\b', logs) or (
                    'failed to exchange h3 connect' in logs and 'unexpected end of input' in logs):
                return True
            time.sleep(0.25)
        print(re.sub(r'(?i)(Bearer )[^\s\"\']+', r'\1[redacted]', logs), flush=True)
        return False

    try:
        for binary, args in [('wstunnel', ['--version']), ('wstunnel-get', ['get-server', '--help']),
                             ('wstunnel-quic', ['server', '--help']),
                             ('AmneziaGeo.Server.Proxy.Host', ['get-server', '--help']),
                             ('AmneziaGeo.Server.Proxy.Host', ['credential'])]:
            executable = '/usr/local/bin/wstunnel' if binary == 'wstunnel' else NATIVE + binary
            output = run('docker', 'run', '--rm', '--network', 'none', '--entrypoint', executable, image, *args)
            check('image contains runnable ' + binary, '--enable-webtransport' in output if binary == 'wstunnel-quic' else bool(output.strip()))
        with tempfile.TemporaryDirectory(prefix=label) as temporary:
            directory = pathlib.Path(temporary)
            key = directory / 'server.key'
            certificate = directory / 'server.crt'
            ca_key = directory / 'ca.key'
            ca = directory / 'ca.crt'
            csr = directory / 'server.csr'
            run('openssl', 'req', '-newkey', 'rsa:2048', '-x509', '-nodes', '-days', '1',
                '-subj', '/CN=image-check-ca', '-addext', 'basicConstraints=critical,CA:TRUE',
                '-addext', 'keyUsage=critical,keyCertSign,cRLSign',
                '-keyout', str(ca_key), '-out', str(ca))
            run('openssl', 'req', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=127.0.0.1',
                '-addext', 'subjectAltName=IP:127.0.0.1', '-addext', 'basicConstraints=critical,CA:FALSE',
                '-addext', 'keyUsage=critical,digitalSignature,keyEncipherment', '-addext', 'extendedKeyUsage=serverAuth',
                '-keyout', str(key), '-out', str(csr))
            extensions = directory / 'server.ext'
            extensions.write_text('subjectAltName=IP:127.0.0.1\nbasicConstraints=critical,CA:FALSE\n'
                                  'keyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n'
                                  'subjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid,issuer\n')
            run('openssl', 'x509', '-req', '-in', str(csr), '-CA', str(ca), '-CAkey', str(ca_key),
                '-CAcreateserial', '-days', '1', '-extfile', str(extensions), '-out', str(certificate))
            run('openssl', 'verify', '-x509_strict', '-CAfile', str(ca), str(certificate))
            key.chmod(0o600)
            ca_key.chmod(0o600)
            pin = hashlib.sha256(ssl.PEM_cert_to_DER_cert(certificate.read_text())).hexdigest()
            (directory / 'catalog.json').write_text('{"version":1,"proxies":[]}')
            owned.append(fixture)
            run('docker', 'run', '-d', '--name', fixture, '--network', 'none', '--label', 'amneziageo.image-check=' + label,
                '--sysctl', 'net.ipv4.ip_forward=1', '--sysctl', 'net.ipv6.conf.all.forwarding=1',
                '--entrypoint', 'sleep', fixture_image, 'infinity')
            run('docker', 'exec', fixture, 'mkdir', '-p', ROOT)
            run('docker', 'cp', str(pathlib.Path(__file__).resolve()), fixture + ':' + ROOT + '/check.py')
            run('docker', 'cp', str(ca), fixture + ':' + ROOT + '/ca.crt')
            run('docker', 'exec', '-d', fixture, 'python', ROOT + '/check.py', '--echo')
            if wg_driver:
                run('docker', 'cp', str(pathlib.Path(wg_driver).resolve()), fixture + ':' + ROOT + '/wg-probe')
            owned.append(server)
            create_server(previous_image or image)
            details = json.loads(run('docker', 'inspect', server))[0]
            check('no host mounts or published ports', all(one['Type'] != 'bind' for one in details['Mounts']) and not details['HostConfig'].get('PortBindings'))
            output = run('docker', 'exec', server, NATIVE + 'AmneziaGeo.Server.Cli', 'token', 'add', 'image-check', '--role', 'admin', '--days', '1')
            token = re.search(r'agt_[^\s]+', output).group(0)
            run('docker', 'exec', server, NATIVE + 'AmneziaGeo.Server.Cli', 'panel', 'set', '--port', '443', '--listen', 'all',
                '--path', 'image-check', '--certificate', ROOT + '/server.crt', '--key', ROOT + '/server.key')
            start_services()
            vpn = None
            if wg_driver:
                config = success('GET', 'configs/draft?name=ag797wg')
                config.update(host='127.0.0.1', listenPort=443, servicesPort=443, webSocket=False,
                              isEnabled=True, opened=False, nat=False, address=['10.79.97.1/24'],
                              allowedIps=['0.0.0.0/0'], presharedKey='')
                config['obfuscation'] = {key: False if isinstance(value, bool) else 0 if isinstance(value, int) else '' for key, value in config['obfuscation'].items()}
                config = success('POST', 'configs', config)
                peer = success('GET', 'clients/draft?config=' + str(config['id']) + '&name=image-client')
                peer = success('POST', 'clients', peer | {'isEnabled': True})
                vpn = {'name': 'image-wg', 'privateKey': peer['privateKey'], 'publicKey': config['publicKey'],
                       'presharedKey': peer.get('presharedKey', ''), 'endpoint': '127.0.0.1:443',
                       'address': peer['address'], 'target': '10.79.97.1:42000',
                       'obfuscation': config['obfuscation'], 'count': 64}
                packet = directory / 'wg.json'
                packet.write_text(json.dumps(vpn))
                packet.chmod(0o600)
                run('docker', 'cp', str(packet), fixture + ':' + ROOT + '/wg.json')
                measured = json.loads(run('docker', 'exec', fixture, ROOT + '/wg-probe', ROOT + '/wg.json'))
                check('direct WG before QUIC', measured.get('received') == 64, received=measured.get('received'))
            draft = success('GET', 'proxies/draft')
            draft.update(name='image-quic1', transport='quic', host='127.0.0.1', port=443, listenPort=0, path='quic1',
                         isEnabled=True, hosted=True, useCertificate=True, pin=pin, allowAnonymous=False, opened=False,
                         targets={'local': False, 'restricted': True, 'destinations': [{'host': '127.0.0.1', 'port': 42000}] + ([{'host': '127.0.0.1', 'port': 443}] if vpn else [])})
            legacy = success('POST', 'proxies', draft | {'name': 'existing-websocket', 'path': 'existing-ws', 'transport': 'websocket'})
            credential = success('POST', 'proxies/' + str(legacy['id']) + '/tokens', {'name': 'before-upgrade'})
            preserved_ids = {legacy['id']}
            if previous_image:
                run('docker', 'stop', '--time', '10', server)
                previous = server + '-previous'
                run('docker', 'rename', server, previous)
                owned.insert(owned.index(server), previous)
                create_server(image, previous)
                start_services()
                held = success('GET', 'proxies')
                check('image upgrade preserves proxy and admin credential', len(held) == 1 and held[0]['id'] == legacy['id'] and held[0]['path'] == legacy['path'] and not held[0].get('fault'))
                if vpn:
                    measured = json.loads(run('docker', 'exec', fixture, ROOT + '/wg-probe', ROOT + '/wg.json'))
                    check('image upgrade preserves WG keys and peer', measured.get('received') == 64, received=measured.get('received'))
            client('existing-ws', credential['secret'], 41005, directory, websocket=True)
            measured = probe(41005)
            check('WebSocket UDP traffic', measured['received'] == 32, **measured)
            for method in ('GET', 'POST'):
                http = success('POST', 'proxies', draft | {'name': 'http-' + method, 'transport': 'http',
                               'path': 'http' + method.lower(), 'httpMethod': method, 'protocolVersion': 2, 'allowAnonymous': True})
                check('HTTP ' + method + ' runtime active', not http.get('fault'), fault=http.get('fault', ''))
                preserved_ids.add(http['id'])
            first = success('POST', 'proxies', draft)
            check('first QUIC listener active', first.get('fault', '') == '', fault=first.get('fault', ''))
            second = success('POST', 'proxies', draft | {'name': 'image-quic2', 'path': 'quic2'})
            check('second QUIC path shares443', second.get('fault', '') == '', fault=second.get('fault', ''))
            minted = success('POST', 'proxies/' + str(first['id']) + '/tokens', {'name': 'test'})
            minted2 = success('POST', 'proxies/' + str(second['id']) + '/tokens', {'name': 'test'})
            duplicate = api('POST', 'proxies', draft | {'name': 'duplicate'})
            check('duplicate path identifies field', duplicate['status'] == 409 and duplicate['body'].get('field') == 'path')
            for path, credential, port in [('quic1', minted['secret'], 41000), ('quic2', minted2['secret'], 41001)]:
                client(path, credential, port, directory)
                measured = probe(port)
                check(path + ' bidirectional UDP over QUIC', measured['received'] == 32, **measured)
            if vpn:
                measured = json.loads(run('docker', 'exec', fixture, ROOT + '/wg-probe', ROOT + '/wg.json'))
                check('direct WG shares443 with QUIC', measured.get('received') == 64, received=measured.get('received'))
                client('quic1', minted['secret'], 41004, directory, target=443)
                vpn['endpoint'] = '127.0.0.1:41004'
                packet.write_text(json.dumps(vpn))
                run('docker', 'cp', str(packet), fixture + ':' + ROOT + '/wg.json')
                measured = json.loads(run('docker', 'exec', fixture, ROOT + '/wg-probe', ROOT + '/wg.json'))
                check('WG traffic through packaged QUIC', measured.get('received') == 64, received=measured.get('received'))
            client('quic1', minted2['secret'], 41002, directory)
            rejected = probe(41002, 1)
            check('credential from another path rejected', rejected['received'] == 0 and refused(41002))
            success('DELETE', 'proxies/' + str(first['id']) + '/tokens/' + minted['token']['id'])
            client('quic1', minted['secret'], 41003, directory)
            rejected = probe(41003, 1)
            check('revoked credential rejected on new connection', rejected['received'] == 0 and refused(41003))
            minted = success('POST', 'proxies/' + str(first['id']) + '/tokens', {'name': 'after-revoke'})
            run('docker', 'restart', server)
            start_services()
            held = success('GET', 'proxies')
            check('restart preserves catalog and runtime', {one['id'] for one in held} == {first['id'], second['id']} | preserved_ids and all(not one.get('fault') for one in held))
            client('quic1', minted['secret'], 41000, directory)
            measured = probe(41000)
            check('QUIC works after restart', measured['received'] == 32, **measured)
            print(json.dumps({'result': 'PASS', 'isolated': isolated}), flush=True)
    finally:
        for name in reversed(owned):
            held = subprocess.run(['docker', 'inspect', name], capture_output=True, text=True, timeout=30)
            if held.returncode == 0 and json.loads(held.stdout)[0]['Config'].get('Labels', {}).get('amneziageo.image-check') == label:
                subprocess.run(['docker', 'rm', '-f', '-v', name], capture_output=True, timeout=30, check=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--image')
    parser.add_argument('--fixture-image', default='python:3.14-slim')
    parser.add_argument('--isolated', action='store_true')
    parser.add_argument('--wg-driver')
    parser.add_argument('--previous-image')
    parser.add_argument('--request', action='store_true')
    parser.add_argument('--echo', action='store_true')
    parser.add_argument('--probe', type=int)
    parser.add_argument('--count', type=int, default=32)
    args = parser.parse_args()
    if args.request:
        request_inside()
    elif args.echo:
        echo_inside()
    elif args.probe:
        probe_inside(args.probe, args.count)
    elif args.image:
        verify(args.image, args.fixture_image, args.isolated, args.wg_driver, args.previous_image)
    else:
        parser.error('--image is required')


if __name__ == '__main__':
    main()
