import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.github' / 'scripts'))
from package_compose import FILES, package


class ComposePackageTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = pathlib.Path(self.folder.name) / 'source'
        self.output = pathlib.Path(self.folder.name) / 'dist'
        source = self.root / 'deploy' / 'compose'
        source.mkdir(parents=True)
        for name in FILES:
            shutil.copy(ROOT / 'deploy' / 'compose' / name, source / name)
        for name in ['docs/compose.md', 'docs/ru/compose.md']:
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(ROOT / name, target)
        self.commit = 'a' * 40
        self.image = 'ghcr.io/bor-project/amneziageo-server'

    def make(self, tag='v1.2.3.4-beta.2'):
        metadata = package(self.root, self.output, tag, self.image, self.commit)
        return metadata, self.output / metadata['name']

    def test_release_channels_select_matching_images(self):
        for tag, version, channel in [('v1.2.3.0', '1.2.3.0', 'stable'), ('v1.2.3.4-beta.2', '1.2.3.4', 'beta'), ('v1.2.3.5-test.1', '1.2.3.5', 'test')]:
            with self.subTest(tag=tag):
                metadata, path = self.make(tag)
                with zipfile.ZipFile(path) as archive:
                    self.assertEqual(set(archive.namelist()), set(FILES) | {'README.md', 'README.ru.md', 'release.json'})
                    self.assertIn('AMNEZIAGEO_TAG=' + version + '\n', archive.read('.env.example').decode())
                    self.assertEqual(json.loads(archive.read('release.json')), {'version': version, 'channel': channel, 'tag': tag, 'commit': self.commit, 'image': self.image + ':' + version})
                    self.assertIn(tag, archive.read('README.ru.md').decode())
                self.assertEqual(metadata['size'], path.stat().st_size)
                self.assertEqual(metadata['sha256'], hashlib.sha256(path.read_bytes()).hexdigest())

    def test_private_files_are_excluded(self):
        source = self.root / 'deploy' / 'compose'
        for name in ['.env', 'server.env', 'bootstrap.json', 'secrets/password', 'certificates/private.key', 'data/server.db', 'compose.override.yaml']:
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('DO-NOT-PUBLISH-PRIVATE-DATA')
        _, path = self.make()
        with zipfile.ZipFile(path) as archive:
            self.assertFalse(any('DO-NOT-PUBLISH-PRIVATE-DATA' in archive.read(name).decode() for name in archive.namelist()))

    def test_package_is_reproducible(self):
        first, path = self.make()
        content = path.read_bytes()
        second, path = self.make()
        self.assertEqual(first, second)
        self.assertEqual(content, path.read_bytes())

    def test_guides_link_to_release_sources(self):
        _, path = self.make()
        with zipfile.ZipFile(path) as archive:
            for name in ['README.md', 'README.ru.md']:
                text = archive.read(name).decode()
                self.assertNotIn('](../', text)
                self.assertNotIn('](compose.md', text)
                self.assertIn('/blob/' + self.commit + '/', text)
                self.assertNotIn('older published images cannot', text)

    def test_invalid_release_inputs_are_rejected(self):
        for tag, image, commit in [('v1.2.3.4', self.image, self.commit), ('dev', self.image, self.commit), ('v1.2.3.0', self.image + '\nSECRET=1', self.commit), ('v1.2.3.0', self.image, 'not-a-commit')]:
            with self.subTest(tag=tag, image=image, commit=commit):
                with self.assertRaises(ValueError):
                    package(self.root, self.output, tag, image, commit)

    def test_missing_version_placeholder_is_rejected(self):
        path = self.root / 'deploy' / 'compose' / '.env.example'
        path.write_text(path.read_text().replace('AMNEZIAGEO_TAG=', 'OTHER_TAG='))
        with self.assertRaises(ValueError):
            self.make()

    @unittest.skipUnless(os.name == 'posix', 'Symlink fixture requires POSIX')
    def test_templates_cannot_redirect_to_private_files(self):
        secret = self.root / 'private-password'
        secret.write_text('DO-NOT-PUBLISH-PRIVATE-DATA')
        path = self.root / 'deploy' / 'compose' / '.env.example'
        path.unlink()
        path.symlink_to(secret)
        with self.assertRaises(ValueError):
            self.make()

    @unittest.skipUnless(os.name == 'posix' and all(shutil.which(tool) for tool in ['bash', 'jq', 'openssl']), 'Release fixture requires bash, jq and OpenSSL')
    def test_release_steps_sign_the_archive_and_upload_only_public_assets(self):
        script = self.root / '.github' / 'scripts'
        script.mkdir(parents=True)
        for name in ['package_compose.py', 'release_policy.py']:
            shutil.copy(ROOT / '.github' / 'scripts' / name, script / name)
        deploy = self.root / 'deploy'
        (deploy / 'amneziageo-server').write_text('release menu fixture')
        release = self.root / 'release'
        release.mkdir()
        for arch in ['x64', 'arm64']:
            for suffix in ['tar.gz', 'files', 'pack']:
                (release / f'amneziageo-server-1.2.3.4-linux-{arch}.{suffix}').write_text(arch + suffix)
            (release / f'amneziageo-updates-1.2.3.4-linux-{arch}.tar.gz').write_text(arch)
        key = pathlib.Path(self.folder.name) / 'signing.pem'
        subprocess.run(['openssl', 'genrsa', '-out', str(key), '2048'], check=True, capture_output=True)
        fake = self.root / 'fake-bin'
        fake.mkdir()
        gh = fake / 'gh'
        gh.write_text('#!/usr/bin/env python3\nimport json,os,sys\nopen(os.environ["GHCAPTURE_FILE"],"w").write(json.dumps(sys.argv[1:]))\n')
        gh.chmod(0o755)
        capture = self.root / 'gh-arguments.json'
        env = dict(os.environ, VERSION='1.2.3.4', RELEASE_TAG='v1.2.3.4-beta.2', CHANNEL='beta', SOURCE_COMMIT=self.commit,
                   IMAGE=self.image, UPDATES_IMAGE='ghcr.io/bor-project/amneziageo-updates', DIGEST='sha256:' + '1' * 64,
                   UPDATES_DIGEST='sha256:' + '2' * 64, SIGNING_KEY=key.read_text(), RUNNER_TEMP=str(self.root),
                   GITHUB_REPOSITORY='bor-project/amneziageo_server', PATH=str(fake) + os.pathsep + os.environ['PATH'],
                   GHCAPTURE_FILE=str(capture))
        lines = (ROOT / '.github' / 'workflows' / 'release.yml').read_text().splitlines()

        def step(name):
            start = next(i for i, line in enumerate(lines) if line.strip() == '- name: ' + name)
            begin = next(i for i in range(start + 1, len(lines)) if lines[i].strip() == 'run: |') + 1
            block = []
            for line in lines[begin:]:
                if line and not line.startswith('          '):
                    break
                block.append(line[10:])
            return '\n'.join(block)

        for name, cwd in [('Package Compose installation', self.root), ('Signed manifest', release), ('Release', release)]:
            result = subprocess.run(['bash', '-e', '-c', step(name)], cwd=cwd, env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, name + '\n' + result.stderr)
        manifest = json.loads((release / 'update.json').read_text())
        archive = release / manifest['compose']['name']
        self.assertEqual(manifest['compose']['sha256'], hashlib.sha256(archive.read_bytes()).hexdigest())
        self.assertEqual(manifest['compose']['size'], archive.stat().st_size)
        self.assertEqual(len(manifest['packages']), 2)
        self.assertEqual({item['arch'] for item in manifest['packages']}, {'x64', 'arm64'})
        public = self.root / 'public.pem'
        subprocess.run(['openssl', 'pkey', '-in', str(key), '-pubout', '-out', str(public)], check=True, capture_output=True)
        result = subprocess.run(['openssl', 'dgst', '-sha256', '-verify', str(public), '-signature', str(release / 'update.json.sig'), str(release / 'update.json')], capture_output=True)
        self.assertEqual(result.returncode, 0)
        arguments = json.loads(capture.read_text())
        self.assertIn(archive.name, arguments)
        self.assertIn('--prerelease', arguments)
        self.assertNotIn('compose-asset.json', arguments)
        self.assertNotIn('bootstrap.json', arguments)
        self.assertEqual(len([name for name in arguments if name.endswith('.zip')]), 1)


if __name__ == '__main__':
    unittest.main()
