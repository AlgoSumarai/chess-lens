import { expect, test } from '@playwright/test';

test('real completed-game analysis produces a recurring insight and an exact saved-review link', async ({ page }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`insight-e2e-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible({ timeout: 15000 });
  const csrf = (await (await page.request.get('/api/account/csrf')).json()).token;
  const headers = { 'X-CSRF-TOKEN': csrf };
  const date = new Date().toISOString().slice(0, 10).replaceAll('-', '.');
  // Labelled synthetic games exercise recurrence with REAL engine evidence.
  // Different Event metadata preserves five distinct source games.
  const pgn = Array.from({ length: 5 }, (_, index) => `[Event "Original recurring-error test fixture ${index}"]\n[Date "${date}"]\n[White "Learner"]\n[Black "Fixture opponent"]\n[TimeControl "600+5"]\n\n1.f3 {[%clk 0:09:55]} e5 {[%clk 0:09:55]} 2.g4 {[%clk 0:00:08]} Qh4# {[%clk 0:09:50]} 0-1`).join('\n\n');
  const imported = await page.request.post('/api/imports/pgn', { headers, data: { pgn, playerName: 'Learner', maxGames: 5 } });
  expect(imported.status()).toBe(202); const importId = (await imported.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/jobs/${importId}`)).json()).state, { timeout: 30000 }).toBe('completed');
  const games = (await (await page.request.get('/api/games')).json()).items as { id: string }[];
  expect(games).toHaveLength(5);
  for (const game of games) {
    const queued = await page.request.post(`/api/games/${game.id}/analysis`, { headers, data: { profile: 'quick' } });
    expect(queued.status()).toBe(202); const run = await queued.json();
    await expect.poll(async () => (await (await page.request.get(`/api/analysis/${run.id}`)).json()).state,
      { timeout: 120000, intervals: [500, 1000, 2000] }).toBe('completed');
  }
  await page.getByRole('link', { name: 'Weaknesses', exact: true }).click();
  await page.getByRole('button', { name: 'Find my patterns', exact: true }).click();
  await expect(page.getByRole('heading', { name: '5 of 5 matching games selected' })).toBeVisible({ timeout: 30000 });
  await expect(page.getByText('Recurring pattern', { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/Time-trouble findings are unavailable/)).toBeVisible();
  await page.getByText(/Inspect \d+ supporting positions/).first().click();
  const link = page.getByRole('link', { name: /Review move/ }).first();
  const href = (await link.getAttribute('href'))!;
  const query = new URLSearchParams(href.split('?')[1]);
  const selectedPly = query.get('ply');
  await page.screenshot({ path: `test-results/weaknesses-${info.project.name}.png`, fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  await link.click();
  await expect(page.getByText('Viewing the saved analysis referenced by this evidence.')).toBeVisible();
  await expect(page.locator(`[aria-label^="Move ${selectedPly}: "]`)).toHaveAttribute('aria-current', 'step');
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('After');
  expect(new URLSearchParams(page.url().split('?')[1]).get('run')).toBe(query.get('run'));
  expect(errors).toEqual([]);
});
