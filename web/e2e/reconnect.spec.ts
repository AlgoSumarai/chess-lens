import { execFileSync } from 'node:child_process';
import { expect, test } from '@playwright/test';

test('an actual API outage misses completion notifications, reconciles saved work and restores owner notifications', async ({ page }, info) => {
  test.setTimeout(300000);
  const project = process.env.CHESSLENS_FAULT_PROJECT;
  test.skip(!project, 'An actual service fault requires an explicitly selected isolated validation project.');
  if (!/^chesslens-(?:[a-z0-9-]+-)?validation$/.test(project!)) throw new Error('Select an isolated chesslens-*-validation project.');
  const api = `${project}-api-1`, database = `${project}-database-1`;
  const docker = (...args: string[]) => execFileSync('docker', ['--context', 'default', ...args], { encoding: 'utf8', timeout: 30000 });
  for (const name of [api, database]) {
    if (docker('inspect', '--format', '{{index .Config.Labels "com.docker.compose.project"}}', name).trim() !== project) throw new Error('Fault target does not match the isolated project.');
  }
  let positionEvents = 0, analysisEvents = 0, connections = 0;
  page.on('websocket', socket => {
    connections++;
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string' || !frame.payload.includes('jobProgress')) return;
      if (frame.payload.includes('"kind":"position"')) positionEvents++;
      if (frame.payload.includes('"kind":"analysis"')) analysisEvents++;
    });
  });
  await page.goto('/'); await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`reconnect-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!'); await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible();
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/account/csrf')).json()).token };
  const imported = await page.request.post('/api/imports/pgn', { headers, data: { pgn: `[Event "Original reconnect fault fixture ${Date.now()}"]\n[White "Learner"]\n[Black "Opponent"]\n[Result "1-0"]\n1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3 d6 8.c3 O-O 9.h3 Nb8 10.d4 Nbd7 11.c4 c6 12.Nc3 Qc7 13.Be3 Bb7 14.Rc1 Rfe8 15.cxb5 axb5 16.Nxb5 1-0`, playerName: 'Learner', maxGames: 1 } });
  expect(imported.ok()).toBe(true); const importId = (await imported.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/jobs/${importId}`)).json()).state, { timeout: 90000 }).toBe('completed');
  const game = (await (await page.request.get('/api/games')).json()).items[0];
  await page.goto(`/#/games/${game.id}`); await page.getByRole('button', { name: 'Deep review', exact: true }).click();
  let runId = '';
  await expect.poll(async () => {
    const run = await (await page.request.get(`/api/games/${game.id}/analysis`)).json(); runId = run?.id ?? '';
    return run?.state === 'running' && run.completedPlies > 0 && run.completedPlies < 31;
  }, { timeout: 60000 }).toBe(true);
  if (!/^[a-f0-9-]{36}$/.test(runId)) throw new Error('Invalid owned fixture run.');
  const rows = () => JSON.parse(docker('exec', database, 'psql', '-X', '-A', '-t', '-U', 'chesslens', '-d', 'chesslens', '-v', 'ON_ERROR_STOP=1', '-c',
    `SELECT json_build_object('state', "State", 'completedPlies', "CompletedPlies", 'attempts', "Attempts", 'moves', (SELECT count(*) FROM "MoveAnalyses" WHERE "RunId"='${runId}')) FROM "AnalysisRuns" WHERE "Id"='${runId}';`).trim());
  await expect.poll(() => analysisEvents, { timeout: 10000 }).toBeGreaterThan(0);
  expect(rows()).toMatchObject({ state: 'running', attempts: 1 });
  const beforeConnections = connections;
  try {
    docker('stop', '--time', '2', api);
    await expect.poll(() => rows().state, { timeout: 150000 }).toBe('completed');
    const completedDuringOutage = rows();
    expect(completedDuringOutage).toMatchObject({ completedPlies: 31, moves: 31, attempts: 1 });
    // Exceed the broadcaster's three-second overlap: completion is genuinely missed,
    // and the browser must recover it from persisted HTTP state after restart.
    await page.waitForTimeout(5000);
    docker('start', api);
    await expect.poll(async () => (await page.request.get('/health/ready')).status(), { timeout: 90000 }).toBe(200);
    await expect(page.getByText('completed · deep review · 31/31 half-moves', { exact: true })).toBeVisible({ timeout: 30000 });
    await expect.poll(() => connections, { timeout: 30000 }).toBeGreaterThan(beforeConnections);
    const beforePositionEvents = positionEvents;
    await page.getByRole('button', { name: 'Move 3: Nf3', exact: true }).click();
    await page.getByRole('button', { name: 'Analyse this position', exact: true }).click();
    await expect(page.getByRole('status', { name: 'Position analysis status' })).toContainText('Position reviewed', { timeout: 60000 });
    await expect.poll(() => positionEvents, { timeout: 15000 }).toBeGreaterThan(beforePositionEvents);
    expect(rows()).toMatchObject({ completedPlies: 31, moves: 31, attempts: 1 });
    await info.attach('actual-reconnect', { body: JSON.stringify({ completedDuringOutage, connectionsBefore: beforeConnections, connectionsAfter: connections, newPositionNotifications: positionEvents - beforePositionEvents }), contentType: 'application/json' });
    await page.screenshot({ path: `test-results/reconnected-${info.project.name}.png`, fullPage: true });
  } finally {
    docker('start', api);
  }
});
