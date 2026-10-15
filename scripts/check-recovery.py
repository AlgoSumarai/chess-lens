#!/usr/bin/env python3
"""Opt-in destructive service-fault check, restricted to an isolated local validation project.

Kills the worker during a real review, stops/restarts its database, waits for the
actual five-minute lease and checks saved evidence and interactive recovery.
Run without other tests or users on that project. Never removes a volume.
"""
import argparse
import datetime as dt
import http.cookiejar
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True)
    parser.add_argument('--url', default='http://localhost:8088')
    parser.add_argument('--output', default='.local/evidence/recovery/native-worker-database.json')
    parser.add_argument('--exhaust-leases', action='store_true', help='Instead exercise four real worker terminations and the unchanged five-minute leases (about 20 minutes).')
    args = parser.parse_args()
    address = urllib.parse.urlparse(args.url)
    if not re.fullmatch(r'chesslens-[a-z0-9-]+-validation', args.project) or address.hostname not in ('localhost', '127.0.0.1') or address.scheme != 'http':
        parser.error('Use a loopback HTTP URL and an isolated chesslens-*-validation project.')
    worker, database = (args.project + suffix for suffix in ('-worker-1', '-database-1'))
    output = Path(args.output); output.parent.mkdir(parents=True, exist_ok=True)
    evidence = {'startedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'project': args.project,
                'kind': 'actual native Docker Engine worker SIGKILL and database outage; original artificial game',
                'commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
                'worktreeDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip()), 'steps': []}

    def docker(*command, check=True):
        return subprocess.run(['docker', '--context', 'default', *command], text=True, capture_output=True, check=check, timeout=45)

    def record(step, **values):
        evidence['steps'].append({'step': step, 'at': dt.datetime.now(dt.timezone.utc).isoformat(), **values})
        output.write_text(json.dumps(evidence, indent=2) + '\n')
        print(step, json.dumps(values), flush=True)

    def sql(query):
        return docker('exec', database, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-U', 'chesslens', '-d', 'chesslens', '-Atc', query).stdout.strip()

    platform = docker('info', '--format', '{{.OperatingSystem}}').stdout.strip()
    if 'Docker Desktop' in platform:
        parser.error('This check requires native Docker Engine.')
    for name in (worker, database):
        label = docker('inspect', '--format', '{{index .Config.Labels "com.docker.compose.project"}}', name).stdout.strip()
        if label != args.project:
            parser.error('Container project label does not match the isolated project.')
    pending = ' + '.join(f'(SELECT count(*) FROM "{table}" WHERE "State" IN (\'waiting\',\'queued\',\'running\'))'
                         for table in ('ImportJobs', 'AnalysisRuns', 'PositionAnalysisJobs', 'Puzzles', 'InsightSnapshots', 'CoachingJobs', 'PracticeJobs', 'ReportSnapshots', 'ImportAnalysisBatches'))
    if int(sql('SELECT ' + pending)):
        parser.error('Wait for existing work before running service faults; the project must be quiescent.')
    record('preflight', engine=platform, dockerVersion=docker('version', '--format', '{{.Server.Version}}').stdout.strip())
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    token = None

    def request(path, body=None, method='GET'):
        headers = {'Content-Type': 'application/json'}
        if token and method != 'GET':
            headers['X-CSRF-TOKEN'] = token
        req = urllib.request.Request(args.url + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
        try:
            with client.open(req, timeout=10) as response:
                raw = response.read()
                return response.status, json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            raw = error.read()
            try: value = json.loads(raw)
            except ValueError: value = None
            return error.code, value

    def api(path, body=None, method='GET'):
        status, value = request('/api' + path, body, method)
        if status >= 400:
            raise AssertionError(f'{method} {path} returned HTTP {status}')
        return value

    def wait(check, seconds, message):
        deadline = time.monotonic() + seconds
        next_update = time.monotonic() + 30
        while time.monotonic() < deadline:
            value = check()
            if value:
                return value
            if time.monotonic() >= next_update:
                print(f'Observing the existing job/service; {int(deadline-time.monotonic())} seconds remain in this check.', flush=True)
                next_update += 30
            time.sleep(.5)
        raise TimeoutError(message)

    registered = False
    try:
        token = api('/account/csrf')['token']
        api('/account/register', {'email': f'recovery-fixture-{uuid.uuid4().hex}@test.example', 'password': 'ChessLens.Recovery2026!' + uuid.uuid4().hex}, 'POST')
        registered = True; token = api('/account/csrf')['token']
        pgn = f'''[Event "Original recovery fixture {uuid.uuid4()}"]
[White "Learner"]
[Black "Opponent"]
[Result "1-0"]

1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5
7.Bb3 d6 8.c3 O-O 9.h3 Nb8 10.d4 Nbd7 11.c4 c6 12.Nc3 Qc7
13.Be3 Bb7 14.Rc1 Rfe8 15.cxb5 axb5 16.Nxb5 1-0'''
        job = api('/imports/pgn', {'pgn': pgn, 'playerName': 'Learner', 'maxGames': 1}, 'POST')['id']
        imported = wait(lambda: (r if (r := api('/jobs/' + job))['state'] not in ('queued', 'running') else None), 60, 'Fixture import did not finish')
        assert imported['state'] == 'completed' and imported['imported'] == 1 and not imported['failures']
        game = api('/games')['items'][0]['id']
        run = api('/games/' + game + '/analysis', {'profile': 'deep'}, 'POST')['id']
        assert str(uuid.UUID(run)) == run
        assert api('/games/' + game + '/analysis', {'profile': 'deep'}, 'POST')['id'] == run
        partial = wait(lambda: (r if (r := api('/analysis/' + run))['state'] == 'running' and r['completedPlies'] >= 1 else None), 90, 'Real review did not produce a partial checkpoint')
        rows_query = f'''SELECT coalesce(json_agg(to_jsonb(m) ORDER BY "Ply"), '[]'::json) FROM "MoveAnalyses" m WHERE "RunId"='{run}'::uuid'''
        before = json.loads(sql(rows_query))
        if args.exhaust_leases:
            evidence['kind'] = 'actual native worker SIGKILL on all four mainline executions, unchanged lease clocks and exhaustion; original artificial game'
            previous_count = 0
            for attempt in range(1, 5):
                def checkpoint():
                    row = api('/analysis/' + run)
                    executions = int(sql(f'''SELECT "Attempts" FROM "AnalysisRuns" WHERE "Id"='{run}'::uuid'''))
                    if row['state'] == 'failed':
                        raise AssertionError('Review failed before the intended execution checkpoint')
                    return row if executions == attempt and row['state'] == 'running' and row['completedPlies'] > previous_count else None
                checkpoint_run = wait(checkpoint, 420, 'Review did not resume after its actual lease expired')
                saved = json.loads(sql(rows_query))
                assert saved[:len(before)] == before
                assert len(saved) < checkpoint_run['totalPlies'], 'Use a longer fixture to retain unfinished work for every termination'
                assert len({row['Ply'] for row in saved}) == len(saved)
                assert api('/games/' + game + '/analysis', {'profile': 'deep'}, 'POST')['id'] == run
                run_count = int(sql(f'''SELECT count(*) FROM "AnalysisRuns" WHERE "GameId"='{game}'::uuid'''))
                assert run_count == 1
                top = docker('top', worker, '-eo', 'pid,comm').stdout.splitlines()[1:]
                engine_pids = [int(line.split()[0]) for line in top if 'stockfish' in line.lower()]
                worker_pid = int(docker('inspect', '--format', '{{.State.Pid}}', worker).stdout)
                assert engine_pids
                lease = sql(f'''SELECT "LeaseUntil" FROM "AnalysisRuns" WHERE "Id"='{run}'::uuid''')
                docker('kill', '--signal', 'KILL', worker)
                docker('stop', '--time', '2', worker)
                wait(lambda: not any(Path('/proc/' + str(pid)).exists() for pid in [worker_pid, *engine_pids]), 10, 'Old worker or engine process survived')
                # A final atomic checkpoint may commit between observing work
                # and SIGKILL. Capture the durable prefix after termination.
                saved = json.loads(sql(rows_query))
                assert saved[:len(before)] == before
                assert len(saved) < checkpoint_run['totalPlies']
                assert len({row['Ply'] for row in saved}) == len(saved)
                record('execution-killed', attempt=attempt, runId=run, completedPlies=len(saved),
                       originalLeaseUntil=lease, oldProcessesReaped=True, duplicateSubmissionsReusedRun=True,
                       prefixEvidenceUnchanged=True, duplicatePlies=0)
                previous_count = len(saved)
                docker('start', worker)
                wait(lambda: request('/health/ready')[0] == 200, 180, 'Restarted worker did not become ready')
                # No database timestamp, attempt or lease mutation is used here.
                # The next loop observes the real persisted lease and claim.
            final = wait(lambda: (r if (r := api('/analysis/' + run))['state'] == 'failed' else None), 420,
                         'The fourth actual expired lease was not marked failed')
            after = json.loads(sql(rows_query))
            assert after == saved and after[:len(before)] == before
            assert len({row['Ply'] for row in after}) == len(after)
            assert int(sql(f'''SELECT "Attempts" FROM "AnalysisRuns" WHERE "Id"='{run}'::uuid''')) == 4
            assert final['completedPlies'] == len(after) and final['error']
            # Allow several scheduler passes, checking that terminal work stays terminal.
            time.sleep(5)
            assert api('/analysis/' + run)['state'] == 'failed'
            assert int(sql(f'''SELECT "Attempts" FROM "AnalysisRuns" WHERE "Id"='{run}'::uuid''')) == 4
            record('leases-exhausted', runId=run, attempts=4, savedPlies=len(after),
                   terminalState='failed', originalLeaseClocksUnchanged=True,
                   duplicatePlies=0, prefixEvidenceUnchanged=True, additionalExecution=False)
            evidence['status'] = 'passed'
            return
        top = docker('top', worker, '-eo', 'pid,comm').stdout.splitlines()[1:]
        engine_pids = [int(line.split()[0]) for line in top if 'stockfish' in line.lower()]
        host_pid = int(docker('inspect', '--format', '{{.State.Pid}}', worker).stdout)
        assert before and engine_pids
        record('partial-review', gameId=game, runId=run, completedPlies=len(before), totalPlies=partial['totalPlies'], workerHostPid=host_pid, engineHostPids=engine_pids,
               leaseUntil=sql(f'''SELECT "LeaseUntil" FROM "AnalysisRuns" WHERE "Id"='{run}'::uuid'''))
        docker('kill', '--signal', 'KILL', worker)
        # Inhibit an automatic policy restart until stale-readiness has been observed.
        docker('stop', '--time', '2', worker)
        wait(lambda: not any(Path('/proc/' + str(pid)).exists() for pid in [host_pid, *engine_pids]), 10, 'Old worker/child processes survived termination')
        wait(lambda: request('/health/ready')[0] == 503, 25, 'Stopped worker readiness did not expire')
        assert request('/health/live')[0] == 200
        record('worker-killed', oldProcessesReaped=True, readiness=503, liveness=200)
        docker('start', worker)
        wait(lambda: request('/health/ready')[0] == 200, 180, 'Worker did not regain readiness')
        assert api('/analysis/' + run)['state'] == 'running'
        record('worker-restarted', preservedRunId=run, waitingForOriginalLease=True)

        docker('stop', '--time', '5', database)
        wait(lambda: request('/health/ready')[0] == 503, 15, 'Database outage did not affect API readiness')
        assert request('/health/live')[0] == 200
        wait(lambda: docker('exec', worker, 'dotnet', 'ChessLens.Worker.dll', '--healthcheck', check=False).returncode == 1, 25, 'Worker health file did not expire during database outage')
        record('database-stopped', apiReadiness=503, apiLiveness=200, workerHealthExit=1)
        docker('start', database)
        wait(lambda: request('/health/ready')[0] == 200, 180, 'Readiness did not recover after database restart')
        record('database-restarted', apiReadiness=200)

        started = time.monotonic()
        position = api('/games/' + game + '/position-analysis', {'channelId': str(uuid.uuid4()), 'sequence': 1, 'ply': 8, 'variation': []}, 'POST')['id']
        candidates = wait(lambda: (r if (r := api('/position-analysis/' + position))['state'] not in ('queued', 'running') else None), 90, 'Interactive recovery did not complete')
        assert candidates['state'] == 'completed' and candidates['result']['complete']
        record('interactive-recovery', positionId=position, elapsedSeconds=round(time.monotonic()-started, 3), completeCandidates=True)

        def reviewed():
            row = api('/analysis/' + run)
            return row if row['state'] not in ('queued', 'running') else None
        final = wait(reviewed, 480, 'Original review did not recover after its actual lease expired')
        assert final['state'] == 'completed', final['error']
        after = json.loads(sql(rows_query))
        assert after[:len(before)] == before, 'Committed prefix evidence changed during resume'
        assert len(after) == final['totalPlies'] and len({row['Ply'] for row in after}) == len(after)
        attempts = int(sql(f'''SELECT "Attempts" FROM "AnalysisRuns" WHERE "Id"='{run}'::uuid'''))
        assert attempts >= 2
        assert api('/games')['total'] == 1
        record('review-resumed', runId=run, attempts=attempts, completedPlies=len(after), prefixEvidenceUnchanged=True, duplicatePlies=0)
        evidence['status'] = 'passed'
    except Exception as error:
        evidence['status'] = 'failed'; record('failure', errorType=type(error).__name__, detail=str(error))
        raise
    finally:
        # Restore services even when an assertion fails. Do not remove data volumes.
        docker('start', database, check=False); docker('start', worker, check=False)
        if registered:
            try:
                wait(lambda: request('/health/ready')[0] == 200, 180, 'Cleanup readiness unavailable')
                api('/account/data', method='DELETE'); evidence['fixtureDataDeleted'] = True
            except Exception:
                evidence['fixtureDataDeleted'] = False
        evidence['finishedAt'] = dt.datetime.now(dt.timezone.utc).isoformat()
        output.write_text(json.dumps(evidence, indent=2) + '\n')


if __name__ == '__main__':
    main()
