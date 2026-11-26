import argparse
import hashlib
import io
import json
import pathlib
import re
import zipfile

from release_policy import SHA, parse_tag


FILES = ('compose.yaml', '.env.example', 'server.env.example', 'bootstrap.example.json')
IMAGE = re.compile(r'ghcr\.io/[a-z0-9][a-z0-9._-]*/[a-z0-9][a-z0-9._-]*(?:/[a-z0-9][a-z0-9._-]*)*')
LINK = re.compile(r'(!?\[[^\]]*\]\()([^\s)]+)(\))')


def public_links(text, document, root, commit):
    def replace(match):
        target = match[2]
        if target.startswith(('#', 'https://', 'http://', 'mailto:')):
            return match[0]
        path, mark, anchor = target.partition('#')
        relative = (document.parent / path).resolve().relative_to(root.resolve()).as_posix()
        return match[1] + f'https://github.com/bor-project/amneziageo_server/blob/{commit}/{relative}' + (mark + anchor) + match[3]

    return LINK.sub(replace, text)


def package(root, output, tag, image, commit):
    version, channel = parse_tag(tag)
    if SHA.fullmatch(commit) is None:
        raise ValueError('Invalid release commit')
    if IMAGE.fullmatch(image) is None:
        raise ValueError('Invalid GHCR image repository')
    source = root / 'deploy' / 'compose'
    if any((source / name).is_symlink() for name in FILES):
        raise ValueError('Compose templates must be regular files')
    entries = {name: (source / name).read_text(encoding='utf-8').replace('\r\n', '\n') for name in FILES}
    env = entries['.env.example']
    for key, value in [('AMNEZIAGEO_IMAGE', image), ('AMNEZIAGEO_TAG', version)]:
        env, count = re.subn(r'^' + key + r'=.*$', key + '=' + value, env, flags=re.MULTILINE)
        if count != 1:
            raise ValueError('Expected exactly one ' + key + ' in the environment example')
    entries['.env.example'] = env
    for name, path in [('README.md', 'docs/compose.md'), ('README.ru.md', 'docs/ru/compose.md')]:
        document = root / path
        text = document.read_text(encoding='utf-8').replace('\r\n', '\n')
        if name == 'README.md':
            title = '# Install with Docker Compose\n\n'
            intro = (f'Release `{tag}`, version `{version}`, channel `{channel}`. '
                     f'Image: `{image}:{version}`.\n\n'
                     'Extract this archive into a private installation directory and run the first-start commands there. '
                     'The image version in `.env.example` is already selected. '
                     'Docker Engine on Linux and Compose 2.30 or newer are required.\n\n')
        else:
            title = '# Установка через Docker Compose\n\n'
            intro = (f'Релиз `{tag}`, версия `{version}`, канал `{channel}`. '
                     f'Образ: `{image}:{version}`.\n\n'
                     'Распакуйте архив в приватный каталог установки и выполняйте команды первого запуска в нём. '
                     'Версия образа уже подставлена в `.env.example`. '
                     'Нужны Linux, Docker Engine и Compose версии 2.30 или новее.\n\n')
        heading = text.find('\n## ')
        if heading < 0:
            raise ValueError('Installation guide has no sections')
        text = title + intro + text[heading + 1:]
        text = text.replace('Copy the contents of `deploy/compose` into a private installation directory, or work in that directory of a\nrepository checkout. Do not copy only `compose.yaml`.', 'In the extracted installation directory:')
        text = text.replace('Скопируйте весь каталог `deploy/compose` в отдельный приватный каталог установки либо работайте в нём в клоне\nрепозитория. Выполните:', 'В каталоге распакованного архива выполните:')
        text = text.replace('AMNEZIAGEO_BUILD_VERSION=0.0.3.22', 'AMNEZIAGEO_BUILD_VERSION=' + version)
        entries[name] = public_links(text, document, root, commit)
    entries['release.json'] = json.dumps({
        'version': version, 'channel': channel, 'tag': tag, 'commit': commit, 'image': image + ':' + version,
    }, ensure_ascii=False, indent=2) + '\n'
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, value in sorted(entries.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, value.encode('utf-8'))
    content = buffer.getvalue()
    name = f'amneziageo-server-compose-{version}.zip'
    output.mkdir(parents=True, exist_ok=True)
    (output / name).write_bytes(content)
    return {'name': name, 'size': len(content), 'sha256': hashlib.sha256(content).hexdigest()}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tag', required=True)
    parser.add_argument('--image', required=True)
    parser.add_argument('--commit', required=True)
    parser.add_argument('--output-dir', type=pathlib.Path, required=True)
    args = parser.parse_args()
    try:
        result = package(pathlib.Path(__file__).resolve().parents[2], args.output_dir, args.tag, args.image, args.commit)
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + '\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
