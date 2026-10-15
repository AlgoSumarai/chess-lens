#!/usr/bin/env python3
"""Opt-in real-engine mixed-workload benchmark on an isolated native Engine stack.

Creates two synthetic owners, queues six reviews and twelve imports, and checks
insight, puzzle, practice, report and interactive progress before backlog drains.
Stops only the selected worker while preparing its queues. Never removes volumes.
Run alone on that project. Docker stats are sampled, not an instantaneous peak.
"""
import argparse
from datetime import datetime, timezone
import http.cookiejar
import json
from pathlib import Path
import re
import subprocess
import threading
import time
import urllib.parse
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True)
    parser.add_argument('--url', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    address = urllib.parse.urlparse(args.url)
    if not re.fullmatch(r'chesslens-[a-z0-9-]+-validation', args.project) or address.scheme != 'http' or address.hostname not in ('localhost', '127.0.0.1') or address.path not in ('', '/'):
        parser.error('Use an isolated validation project and loopback HTTP URL.')
    args.output.mkdir(parents=True, exist_ok=False)
    worker = args.project + '-worker-1'
    database = args.project + '-database-1'
    services = [args.project + '-' + s + '-1' for s in ('api', 'worker', 'database', 'frontend')]
    report = {'startedUtc': datetime.now(timezone.utc).isoformat(), 'project': args.project,
              'commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
              'worktreeDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip()),
              'engine': {'version': 'Stockfish 19', 'maximumProcesses': 2, 'threadsPerProcess': 1, 'hashMiBPerProcess': 64},
              'samples': [], 'events': [], 'limitations': [
                  'One local run of original artificial games; not a capacity or native ARM benchmark.',
                  'Docker stats sampled about every two seconds; sampled peaks can miss shorter spikes.',
                  'Preparation and one six-ply deep review are excluded from measured workload time.',
                  'Normal compatible cache remains enabled; source histories share opening positions.',
                  'Worker health probes and PostgreSQL/HTTP polling remain enabled.']}

    def docker(*command):
        return subprocess.check_output(['docker', '--context', 'default', *command], text=True, stderr=subprocess.PIPE, timeout=60).strip()

    def sql(query):
        return docker('exec', database, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'chesslens', '-d', 'chesslens', '-Atc', query)

    def save():
        (args.output / 'benchmark.json').write_text(json.dumps(report, indent=2) + '\n')

    def event(kind, **values):
        report['events'].append({'kind': kind, 'elapsedSeconds': round(time.monotonic() - started, 3), **values})
        save()
        print(kind, json.dumps(values), flush=True)

    class Client:
        def __init__(self):
            self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
            self.token = None

        def api(self, path, body=None, method='GET'):
            headers = {'Content-Type': 'application/json'}
            if self.token and method != 'GET':
                headers['X-CSRF-TOKEN'] = self.token
            request = urllib.request.Request(args.url.rstrip('/') + '/api' + path,
                      data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
            with self.opener.open(request, timeout=15) as response:
                raw = response.read()
                return json.loads(raw) if raw else None

        def register(self):
            self.token = self.api('/account/csrf')['token']
            self.api('/account/register', {'email': 'benchmark-' + uuid.uuid4().hex + '@test.example',
                     'password': 'ChessLens.Benchmark2026!' + uuid.uuid4().hex}, 'POST')
            self.token = self.api('/account/csrf')['token']

    def wait(check, seconds=180):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            result = check()
            if result:
                return result
            time.sleep(1)
        raise TimeoutError('The measured operation did not finish within its observation bound.')

    def terminal(client, path, states=('completed',)):
        row = client.api(path)
        if row['state'] in ('failed', 'cancelled', 'rejected'):
            raise AssertionError(f'{path} reached {row["state"]}')
        return row if row['state'] in states else None

    def pending():
        tables = ('ImportJobs', 'AnalysisRuns', 'PositionAnalysisJobs', 'Puzzles', 'InsightSnapshots', 'PracticeJobs', 'ReportSnapshots')
        return {t: int(sql(f'''SELECT count(*) FROM "{t}" WHERE "State" IN ('waiting','queued','running')''')) for t in tables}

    engine = docker('info', '--format', '{{.OperatingSystem}}')
    if 'Docker Desktop' in engine:
        parser.error('Use native Linux Docker Engine.')
    for name in services:
        if docker('inspect', '--format', '{{index .Config.Labels "com.docker.compose.project"}}', name) != args.project:
            parser.error('Compose ownership label mismatch.')
    if any(pending().values()):
        parser.error('The validation stack must have no pending work before preparation.')
    report['hardware'] = {'cpu': subprocess.check_output(['lscpu'], text=True),
                          'memory': Path('/proc/meminfo').read_text().splitlines()[:3],
                          'kernel': subprocess.check_output(['uname', '-srmo'], text=True).strip(),
                          'dockerEngine': docker('version', '--format', '{{.Server.Version}}'), 'os': engine}
    report['images'] = {name: docker('inspect', '--format', '{{.Image}}', name) for name in services}
    report['workerLimits'] = json.loads(docker('inspect', '--format', '{"nanoCpus":{{.HostConfig.NanoCpus}},"memoryBytes":{{.HostConfig.Memory}}}', worker))
    report['baselineDockerStats'] = [[json.loads(line) for line in docker('stats', '--no-stream', '--format', '{{json .}}', *services).splitlines()] for _ in range(3)]
    report['baselineHostLoad'] = Path('/proc/loadavg').read_text().strip()
    clients = []
    stopped = threading.Event()
    sampling = None
    started = time.monotonic()
    try:
        for _ in range(2):
            client = Client(); client.register(); clients.append(client)
        today = datetime.now(timezone.utc).date().isoformat()
        date = today.replace('-', '.')

        def pgn(label, moves, black=False):
            return f'[Event "Original benchmark {label} {uuid.uuid4()}"]\n[Date "{date}"]\n[White "{"Opponent" if black else "Learner"}"]\n[Black "{"Learner" if black else "Opponent"}"]\n[Result "0-1" ]\n[TimeControl "300+0"]\n\n{moves} 0-1'

        lines = [
            '1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3 d6 8.c3 O-O 9.h3 Nb8 10.d4 Nbd7 11.c4 c6 12.Nc3 Qc7 13.Be3 Bb7 14.Rc1 Rfe8 15.cxb5 axb5 16.Nxb5',
            '1.d4 d5 2.c4 e6 3.Nc3 Nf6 4.Bg5 Be7 5.e3 O-O 6.Nf3 h6 7.Bh4 b6 8.cxd5 Nxd5 9.Bxe7 Qxe7 10.Nxd5 exd5 11.Rc1 Be6 12.Qa4 c5 13.Qa3 Rc8 14.Bb5 a6 15.dxc5 bxc5',
            '1.e4 c5 2.Nf3 d6 3.d4 cxd4 4.Nxd4 Nf6 5.Nc3 a6 6.Be3 e5 7.Nb3 Be6 8.f3 Be7 9.Qd2 O-O 10.O-O-O Nbd7 11.g4 b5 12.g5 b4 13.Ne2 Nh5 14.f4 a5 15.f5 Bc4']
        seed_pgn = pgn('seed', '1.f3 e5 2.g4 Nc6 3.a3 Qh4#', black=True)
        for index, client in enumerate(clients):
            batch = seed_pgn + '\n\n' + '\n\n'.join(pgn(f'{index}-{i}', line) for i, line in enumerate(lines))
            job = client.api('/imports/pgn', {'pgn': batch, 'playerName': 'Learner', 'maxGames': 4}, 'POST')
            done = wait(lambda: terminal(client, '/jobs/' + job['id']))
            assert done['imported'] == 4 and not done['failures']
        games = [client.api('/games')['items'] for client in clients]
        seed = next(g for g in games[0] if g['plies'] == 6)
        seed_run = clients[0].api('/games/' + seed['id'] + '/analysis', {'profile': 'deep'}, 'POST')['id']
        wait(lambda: terminal(clients[0], '/analysis/' + seed_run), 240)
        report['preparationSeconds'] = round(time.monotonic() - started, 3)
        docker('stop', '--time', '10', worker)
        reviews = []
        imports = []
        for index, client in enumerate(clients):
            for game in (g for g in games[index] if g['plies'] > 6):
                profile = 'quick' if index == 0 else 'deep'
                run = client.api('/games/' + game['id'] + '/analysis', {'profile': profile}, 'POST')
                reviews.append({'client': index, 'id': run['id'], 'profile': profile, 'plies': run['totalPlies']})
            for i in range(6):
                job = client.api('/imports/pgn', {'pgn': pgn(f'backlog-{index}-{i}', '1.a3 a6 2.h3 h6'), 'playerName': 'Learner', 'maxGames': 1}, 'POST')
                imports.append({'client': index, 'id': job['id']})
        primary = clients[0]
        insight = primary.api('/insights', {'from': today + 'T00:00:00Z', 'to': today + 'T23:59:59Z'}, 'POST')['id']
        puzzle = primary.api('/puzzles', {'runId': seed_run, 'ply': 4}, 'POST')['id']
        practice = primary.api('/practice', {'runId': seed_run, 'ply': 4}, 'POST')['id']
        progress = primary.api('/progress', {'fromDate': today, 'throughDate': today}, 'POST')['id']
        position = primary.api('/games/' + seed['id'] + '/position-analysis', {'channelId': str(uuid.uuid4()), 'sequence': 1, 'ply': 4, 'variation': []}, 'POST')['id']
        report['workload'] = {'reviews': reviews, 'reviewGames': len(reviews), 'reviewPlies': sum(r['plies'] for r in reviews),
            'queuedImports': len(imports), 'queuedImportGames': 12, 'queuedImportPlies': 48,
            'sideJobs': {'insight': insight, 'puzzle': puzzle, 'practice': practice, 'progressReport': progress, 'position': position},
            'profiles': {'quick': {'nodes': 30000, 'maxMilliseconds': 1000}, 'deep': {'nodes': 250000, 'maxMilliseconds': 5000}},
            'verification': 'Production rechecks deepen flagged or bounded decisions; exact persisted search budgets are exported after completion.'}
        started = time.monotonic()
        report['measuredStartedUtc'] = datetime.now(timezone.utc).isoformat()

        def sample():
            while not stopped.is_set():
                try:
                    stats = [json.loads(line) for line in docker('stats', '--no-stream', '--format', '{{json .}}', *services).splitlines()]
                    engines = [line for line in docker('top', worker, '-eo', 'pid,comm').splitlines()[1:] if 'stockfish' in line.lower()]
                    report['samples'].append({'elapsedSeconds': round(time.monotonic() - started, 3), 'services': stats, 'engineProcesses': len(engines)})
                except subprocess.SubprocessError as error:
                    report.setdefault('samplingErrors', []).append(type(error).__name__)
                stopped.wait(.25)

        docker('start', worker)
        sampling = threading.Thread(target=sample, daemon=True); sampling.start()
        observed = set()
        side = {'insight': ('/insights/' + insight, ('completed',)), 'puzzle': ('/puzzles/' + puzzle, ('ready',)),
                'practice': ('/practice/' + practice, ('active',)), 'report': ('/reports/' + progress, ('completed',)),
                'position': ('/position-analysis/' + position, ('completed',))}
        deadline = started + 1800
        while time.monotonic() < deadline:
            for name, (path, states) in side.items():
                if name not in observed and terminal(primary, path, states):
                    counts = pending()
                    assert counts['AnalysisRuns'] > 0 and counts['ImportJobs'] > 0, 'Side job completed only after backlog drained.'
                    observed.add(name); event('side-job-progress', lane=name, pending=counts)
            for review in reviews:
                if review['id'] not in observed:
                    row = terminal(clients[review['client']], '/analysis/' + review['id'])
                    if row:
                        assert row['completedPlies'] == review['plies']
                        observed.add(review['id']); event('review-completed', **review, elapsedEngineMilliseconds=row['elapsedMilliseconds'], peakEngineMemoryBytes=row['peakEngineMemoryBytes'])
            for job in imports:
                if job['id'] not in observed:
                    row = terminal(clients[job['client']], '/jobs/' + job['id'])
                    if row:
                        assert not row['failures']
                        observed.add(job['id']); event('import-completed', **job, imported=row['imported'], duplicates=row['duplicates'])
            if len(observed) == len(side) + len(reviews) + len(imports):
                break
            time.sleep(4)
        else:
            raise TimeoutError('The mixed workload exceeded 30 minutes.')
        report['elapsedSeconds'] = round(time.monotonic() - started, 3)
        assert report['samples'] and max(s['engineProcesses'] for s in report['samples']) <= 2
        ids = ','.join("'" + str(uuid.UUID(r['id'])) + "'" for r in reviews)
        report['persistedSearchEvidence'] = json.loads(sql(f'''SELECT json_agg(to_jsonb(s)) FROM (SELECT "RunId", "Ply", "BudgetJson"::jsonb AS "budget", "BestSearchJson"::jsonb AS "best", "PlayedSearchJson"::jsonb AS "played" FROM "MoveAnalyses" WHERE "RunId" IN ({ids}) ORDER BY "RunId","Ply") s'''))
        report['status'] = 'passed'
    except Exception as error:
        report['status'] = 'failed'; report['failure'] = {'type': type(error).__name__, 'detail': str(error)}
        raise
    finally:
        stopped.set()
        if sampling:
            sampling.join(timeout=65)
        docker('start', worker)
        for client in clients:
            try:
                client.api('/account/data', method='DELETE')
                assert client.api('/games')['total'] == 0
            except Exception as error:
                report.setdefault('cleanupErrors', []).append(type(error).__name__)
        report['fixtureDataDeleted'] = not bool(report.get('cleanupErrors'))
        report['finishedUtc'] = datetime.now(timezone.utc).isoformat(); save()


if __name__ == '__main__':
    main()
