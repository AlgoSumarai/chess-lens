#!/usr/bin/env python3
"""Package or verify an offline local release; never publishes images or secrets."""
import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def docker(*args):
    return subprocess.check_output(['docker', '--context', 'default', *args], text=True, stderr=subprocess.PIPE, timeout=120).strip()


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inspect(reference, platform):
    raw = json.loads(docker('image', 'inspect', '--platform', platform, reference))[0]
    if platform != raw['Os'] + '/' + raw['Architecture']:
        raise ValueError('Image architecture mismatch.')
    return {key: raw[key] for key in ('Id', 'Os', 'Architecture', 'Size', 'RepoDigests')}


def verify(directory, selected_platform=None):
    receipt = dict(line.split('  ', 1)[::-1] for line in (directory / 'SHA256SUMS').read_text().splitlines())
    if receipt.get('release.json') != sha(directory / 'release.json'):
        raise ValueError('Release manifest checksum mismatch.')
    manifest = json.loads((directory / 'release.json').read_text())
    for relative, expected in manifest['files'].items():
        path = directory / relative
        if path.resolve().is_relative_to(directory.resolve()) is False or sha(path) != expected:
            raise ValueError('Release file checksum mismatch: ' + relative)
    checked = 0
    for platform, images in manifest['images'].items():
        if selected_platform is not None and platform != selected_platform:
            continue
        for item in images.values():
            if inspect(item['reference'], platform)['Id'] != item['Id']:
                raise ValueError('Load the correct archive; local image differs: ' + item['reference'])
            checked += 1
    print(f'Verified release file hashes and {checked} local runtime image IDs.')


def package(args):
    if subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip():
        raise ValueError('Commit the intended source before packaging; preserve unrelated changes.')
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    application_commit = subprocess.check_output(['git', 'rev-parse', args.application_commit], text=True).strip()
    changed = subprocess.check_output(['git', 'diff', '--name-only', application_commit, commit, '--',
              'src', 'web', 'Dockerfile', '.dockerignore', 'Directory.Build.props', 'global.json', 'samples/first-review.pgn', 'compose.yaml', 'compose.app.yaml', 'compose.production.yaml', 'deploy', 'licenses'], text=True)
    if changed.strip():
        raise ValueError('Application inputs changed since the selected image source commit; rebuild first.')
    if not re.fullmatch(r'[a-z0-9][a-z0-9.-]{0,63}', args.version):
        raise ValueError('Use a short lowercase release version.')
    mapping = json.loads(args.images.read_text())
    if set(mapping) != {'linux/amd64', 'linux/arm64'} or any(set(images) != {'api', 'worker', 'frontend', 'database'} for images in mapping.values()):
        raise ValueError('Map exactly four runtime images for each supported platform.')
    args.output.mkdir(parents=True, exist_ok=False)
    manifest = {'version': args.version, 'createdUtc': datetime.now(timezone.utc).isoformat(),
                'sourceCommit': commit, 'applicationCommit': application_commit,
                'scope': 'Offline local preparation; no registry publication or public deployment.', 'images': {}}
    subprocess.run(['git', 'archive', '--format=tar.gz', '--output=' + str(args.output.resolve() / 'source.tar.gz'), commit], check=True)
    for platform, images in mapping.items():
        architecture = platform.split('/')[1]
        notices = getattr(args, 'notices_' + architecture)
        notice_inventory = json.loads((notices / 'manifest.json').read_text())
        notice_ids = {item['imageId'] for item in notice_inventory['images'] if item['platform'] == platform}
        manifest['images'][platform] = {}
        references = []
        for service, reference in images.items():
            metadata = inspect(reference, platform)
            if metadata['Id'] not in notice_ids:
                raise ValueError('Collect exact image notices before packaging: ' + reference)
            release_reference = f'chesslens-release-{service}:{args.version}-{architecture}'
            docker('tag', reference, release_reference)
            metadata['reference'] = release_reference
            manifest['images'][platform][service] = metadata
            references.append(release_reference)
        shutil.copytree(notices, args.output / ('notices-' + architecture))
        override = 'services:\n' + ''.join(f'  {service}:\n    image: {manifest["images"][platform][image]["reference"]}\n    platform: {platform}\n    pull_policy: never\n'
                   for service, image in [('api', 'api'), ('migrate', 'api'), ('worker', 'worker'), ('frontend', 'frontend'), ('database', 'database')])
        (args.output / ('compose-' + architecture + '.yaml')).write_text(override)
        archive = args.output / ('images-' + architecture + '.tar.gz')
        with archive.open('xb') as target, tempfile.TemporaryFile() as errors:
            process = subprocess.Popen(['docker', '--context', 'default', 'image', 'save', '--platform', platform, *references], stdout=subprocess.PIPE, stderr=errors)
            try:
                with gzip.GzipFile(filename='', fileobj=target, mode='wb', compresslevel=1, mtime=0) as zipped:
                    shutil.copyfileobj(process.stdout, zipped, length=1024 * 1024)
                if process.wait(timeout=600):
                    errors.seek(0)
                    raise RuntimeError('Image archive failed: ' + errors.read(2000).decode(errors='replace'))
            finally:
                if process.poll() is None:
                    process.kill(); process.wait(timeout=15)
                process.stdout.close()
        print('Saved', platform, 'runtime images and exact notices.', flush=True)
    manifest['files'] = {str(p.relative_to(args.output)): sha(p) for p in sorted(args.output.rglob('*')) if p.is_file()}
    (args.output / 'release.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (args.output / 'SHA256SUMS').write_text(''.join(value + '  ' + relative + '\n' for relative, value in manifest['files'].items()) + sha(args.output / 'release.json') + '  release.json\n')
    verify(args.output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    create = commands.add_parser('package')
    create.add_argument('--images', type=Path, required=True)
    create.add_argument('--application-commit', required=True)
    create.add_argument('--notices-amd64', type=Path, required=True)
    create.add_argument('--notices-arm64', type=Path, required=True)
    create.add_argument('--version', required=True)
    create.add_argument('--output', type=Path, required=True)
    check = commands.add_parser('verify')
    check.add_argument('directory', type=Path)
    check.add_argument('--platform', choices=('linux/amd64', 'linux/arm64'))
    args = parser.parse_args()
    if 'Docker Desktop' in docker('info', '--format', '{{.OperatingSystem}}'):
        parser.error('Use native Linux Docker Engine.')
    if args.command == 'package':
        package(args)
    else:
        verify(args.directory, args.platform)


if __name__ == '__main__':
    main()
