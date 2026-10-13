#!/usr/bin/env python3
"""Verify Docker DNS recovery after API recreation in an isolated native Engine project.

Briefly removes only the API container, retaining its named key volume. A bounded
sleeping container occupies its old IP so the recreated API must get a new one.
The frontend container is never restarted. Run without other tests on the project.
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True)
    parser.add_argument('--url', default='http://localhost:8088')
    parser.add_argument('--output', default='.local/evidence/recovery/native-proxy-recreation.json')
    args = parser.parse_args()
    url = urllib.parse.urlparse(args.url)
    if not re.fullmatch(r'chesslens-[a-z0-9-]+-validation', args.project) or url.scheme != 'http' or url.hostname not in ('localhost', '127.0.0.1'):
        parser.error('Use an isolated chesslens-*-validation project and loopback HTTP URL.')
    api, frontend, holder, network = (args.project + suffix for suffix in ('-api-1', '-frontend-1', '-ip-holder', '_default'))
    env = dict(os.environ, DATABASE_PORT=os.environ.get('DATABASE_PORT', '54339'), APP_PORT=str(url.port or 80))

    def docker(*command, check=True):
        return subprocess.run(['docker', '--context', 'default', *command], env=env, check=check, text=True, capture_output=True, timeout=45).stdout.strip()

    def compose(*command):
        return docker('compose', '-p', args.project, '-f', 'compose.yaml', '-f', 'compose.app.yaml', *command)

    def inspect(container, field):
        return docker('inspect', '--format', '{{' + field + '}}', container)

    def address(container):
        return json.loads(inspect(container, 'json .NetworkSettings.Networks'))[network]['IPAddress']

    def ready():
        try:
            with urllib.request.urlopen(args.url + '/health/ready', timeout=5) as response:
                return response.status == 200
        except (urllib.error.URLError, TimeoutError):
            return False

    assert 'Docker Desktop' not in docker('info', '--format', '{{.OperatingSystem}}')
    assert inspect(api, 'index .Config.Labels "com.docker.compose.project"') == args.project
    assert ready(), 'Start with a ready application.'
    evidence = {'startedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'project': args.project, 'status': 'running',
                'frontendId': inspect(frontend, '.Id'), 'oldApiId': inspect(api, '.Id'), 'oldApiIp': address(api)}
    image = inspect(api, '.Config.Image')
    output = Path(args.output); output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, indent=2) + '\n')
    try:
        compose('stop', 'api'); compose('rm', '-f', 'api')
        docker('run', '-d', '--name', holder, '--network', network, '--ip', evidence['oldApiIp'], '--memory', '16m', '--entrypoint', '/bin/sleep', image, '180')
        compose('up', '-d', '--no-deps', '--no-build', 'api')
        evidence['newApiIp'] = address(api); evidence['newApiId'] = inspect(api, '.Id')
        assert evidence['newApiIp'] != evidence['oldApiIp'] and evidence['newApiId'] != evidence['oldApiId']
        started = time.monotonic()
        while not ready():
            if time.monotonic() - started > 45:
                raise TimeoutError('Frontend did not rediscover the recreated API.')
            time.sleep(.5)
        evidence['recoverySeconds'] = round(time.monotonic() - started, 3)
        assert inspect(frontend, '.Id') == evidence['frontendId']
        evidence.update(status='passed', frontendUnchanged=True)
        print(json.dumps(evidence, indent=2), flush=True)
    except Exception as error:
        evidence.update(status='failed', errorType=type(error).__name__, detail=str(error))
        raise
    finally:
        docker('rm', '-f', holder, check=False)
        compose('up', '-d', '--no-deps', '--no-build', 'api')
        evidence['finishedAt'] = dt.datetime.now(dt.timezone.utc).isoformat()
        output.write_text(json.dumps(evidence, indent=2) + '\n')


if __name__ == '__main__':
    main()
