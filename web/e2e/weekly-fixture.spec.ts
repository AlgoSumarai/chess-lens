import { execFileSync } from 'node:child_process';
import { expect, test } from '@playwright/test';

test('a labelled past completion timestamp exercises saved weekly reports and timezone changes with real engine evidence', async ({ page }, info) => {
  const database = process.env.CHESSLENS_TEST_DATABASE_CONTAINER;
  test.skip(!database, 'Past completion dates require an explicitly selected isolated validation database; this is a labelled temporal fixture.');
  if (!/^chesslens-validation-[a-z0-9-]*database-1$/.test(database!)) throw new Error('Only an isolated chesslens-validation database container is allowed.');
  await page.goto('/'); await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`weekly-fixture-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!'); await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible({ timeout: 15000 });
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/account/csrf')).json()).token };
  const imported = await page.request.post('/api/imports/pgn', { headers, data: { pgn: `[Event "Original weekly temporal fixture ${Date.now()}"]\n[White "Opponent"]\n[Black "Learner"]\n1.f3 e5 2.g4 Nc6 3.a3 Qh4# 0-1`, playerName: 'Learner', maxGames: 1 } });
  const importId = (await imported.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/jobs/${importId}`)).json()).state, { timeout: 30000 }).toBe('completed');
  const game = (await (await page.request.get('/api/games')).json()).items[0];
  const run = await page.request.post(`/api/games/${game.id}/analysis`, { headers, data: { profile: 'deep' } }); const runId = (await run.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/analysis/${runId}`)).json()).state, { timeout: 120000 }).toBe('completed');
  const calendar = await (await page.request.get('/api/reports/calendar')).json();
  const fixtureTimestamp = new Date(new Date(calendar.latestCompleted.from).getTime() + 86400000).toISOString();
  if (!/^[a-f0-9-]{36}$/.test(runId)) throw new Error('Invalid fixture run ID');
  // Only this newly created fixture run is dated retrospectively. Moves, scores and engine provenance are real.
  execFileSync('docker', ['exec', '-i', database!, 'psql', '-U', 'chesslens', '-d', 'chesslens', '-v', 'ON_ERROR_STOP=1', '-q'], {
    input: `UPDATE "AnalysisRuns" SET "UpdatedAt" = '${fixtureTimestamp}' WHERE "Id" = '${runId}' AND "State" = 'completed';`,
    stdio: ['pipe', 'pipe', 'pipe'], timeout: 10000,
  });
  await page.getByRole('link', { name: 'Weekly reports', exact: true }).click();
  await page.getByRole('button', { name: 'Generate saved report', exact: true }).click();
  await expect(page.getByRole('status', { name: 'Report status' })).toHaveText('Saved report ready.', { timeout: 30000 });
  await expect(page.getByRole('heading', { name: 'Your weekly reflection', exact: true })).toBeVisible();
  await expect(page.getByText('1 selected games have no played-at date', { exact: false })).toBeVisible();
  const savedUrl = page.url(); const id = savedUrl.split('/reports/')[1];
  const original = (await (await page.request.get(`/api/reports/${id}`)).json());
  expect(original.period.timezone).toBe('UTC'); expect(original.result.results.wins).toBe(1);
  await page.getByRole('link', { name: 'Weekly reports', exact: true }).click(); await page.getByRole('button', { name: 'Generate saved report', exact: true }).click();
  await expect(page).toHaveURL(savedUrl);
  await page.getByRole('link', { name: 'Settings', exact: true }).click(); await page.getByLabel('Timezone', { exact: true }).fill('Africa/Johannesburg');
  await page.getByRole('button', { name: 'Save preferences', exact: true }).click(); await expect(page.getByRole('status')).toContainText('Preferences saved.');
  await page.goto(savedUrl); await expect(page.getByRole('status', { name: 'Report status' })).toHaveText('Saved report ready.');
  expect((await (await page.request.get(`/api/reports/${id}`)).json()).result).toEqual(original.result);
  await page.screenshot({ path: `test-results/weekly-temporal-fixture-${info.project.name}.png`, fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  await page.getByRole('link', { name: 'Weekly reports', exact: true }).click(); await expect(page.getByText(/Monday through Sunday in Africa\/Johannesburg/)).toBeVisible();
  await page.getByRole('button', { name: 'Generate saved report', exact: true }).click(); await expect(page).not.toHaveURL(savedUrl);
  await expect(page.getByRole('status', { name: 'Report status' })).toHaveText('Saved report ready.', { timeout: 30000 });
  expect((await (await page.request.get(`/api/reports/${page.url().split('/reports/')[1]}`)).json()).period.timezone).toBe('Africa/Johannesburg');
});
