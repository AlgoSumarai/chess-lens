import { expect, test } from '@playwright/test';
import { auditAccessibility } from './accessibility';
import { browserTimeout } from './environment';

async function register(page: import('@playwright/test').Page, label: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`${label}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible();
  await page.getByRole('link', { name: 'Import your first games' }).click();
}

test('a real PGN batch is reviewed durably and resumes after reload', async ({ page }, info) => {
  await register(page, `batch-${info.project.name}`);
  const pgn = Array.from({ length: 4 }, (_, i) => `[Event "Original batch fixture ${Date.now()}-${i}"]\n[White "Learner"]\n[Black "Opponent"]\n[Result "0-1"]\n\n1. f3 e5 2. g4 Qh4# 0-1`).join('\n\n');
  await page.getByLabel('Or paste PGN').fill(pgn);
  await page.getByLabel('Your player name').fill('Learner');
  await page.getByRole('button', { name: 'Import games', exact: true }).click();
  await expect(page.getByText('4 imported · 0 already in library · 0 could not be imported')).toBeVisible({ timeout: 30000 });
  await page.getByRole('button', { name: 'Review this batch', exact: true }).click();
  await page.getByLabel('Games to review').fill('4');
  await page.getByRole('button', { name: 'Analyse selected games', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Batch review', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: '4/4 games reviewed', exact: true })).toBeVisible({ timeout: 150000 });
  await expect(page.getByRole('status')).toContainText('16/16 half-moves reviewed');
  await page.screenshot({ path: `test-results/batch-${info.project.name}.png`, fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  await page.getByRole('link', { name: 'Game 1', exact: true }).click();
  await expect(page.getByText('completed · quick review · 4/4 half-moves')).toBeVisible();
  await page.getByRole('button', { name: 'Move 3: g4', exact: true }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('After g4');
});

test('public-provider form and failure recovery remain usable (labelled browser response fixture)', async ({ page }, info) => {
  await register(page, `provider-ui-${info.project.name}`);
  const id = '11111111-2222-4333-8444-555555555555';
  const providerRequest = { provider: 'lichess', username: 'A_very_long_public_username', from: '2026-09-01T00:00:00Z', to: '2026-10-01T00:00:00.000Z', maxGames: 2 };
  let submissions = 0;
  await page.route('**/api/imports/provider', async route => {
    expect(route.request().postDataJSON()).toEqual(providerRequest);
    submissions++;
    await route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify({ id }) });
  });
  await page.route('**/api/jobs', route => route.fulfill({ contentType: 'application/json', body: JSON.stringify(submissions ? [{ id,
    source: 'lichess', state: 'failed', phase: 'fetching', imported: 0, duplicates: 0, processed: 0, total: 0, failures: [],
    error: 'Browser response fixture: the public export is unavailable. Check the username or try later.', version: 2,
    cancelRequested: false, nextAttemptAt: null, request: providerRequest }] : []) }));
  await page.getByLabel('Import from').selectOption('lichess');
  await page.getByLabel('Public username').fill(providerRequest.username);
  await page.getByLabel('From date (UTC)').fill('2026-09-01');
  await page.getByLabel('Through date (UTC)').fill('2026-09-30');
  await page.getByLabel('Maximum games').fill('2');
  await page.getByRole('button', { name: 'Import games', exact: true }).click();
  await expect(page.getByText(/Browser response fixture: the public export is unavailable/)).toBeVisible();
  await page.getByRole('button', { name: 'Retry import', exact: true }).click();
  await expect.poll(() => submissions).toBe(2);
  await page.screenshot({ path: `test-results/provider-failure-${info.project.name}.png`, fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
});

test('malformed optional clocks preserve mixed import progress and usable later games', async ({ page }, info) => {
  await register(page, `clock-boundary-${info.project.name}`);
  const header = `[Event "Original clock-boundary browser fixture ${Date.now()}"]\n[White "Learner"]\n[Black "Opponent"]\n`;
  const pgn = `${header}1.e4 {[%clk 999999999999999999999:00:00]} e5 1/2-1/2\n\n[White "Illegal fixture"]\n1.e5 e4 0-1\n\n${header}1.d4 {[%clk 0:05:00]} d5 1/2-1/2`;
  await page.getByLabel('Or paste PGN').fill(pgn);
  await page.getByLabel('Your player name').fill('Learner');
  await page.getByRole('button', { name: 'Import games', exact: true }).click();
  await expect(page.getByText('2 imported · 0 already in library · 1 could not be imported')).toBeVisible({ timeout: browserTimeout(30000) });
  await page.getByText('View per-game issues', { exact: true }).click();
  await expect(page.getByText(/Game 2: Illegal or malformed move/)).toBeVisible();
  await expect(page.getByText('completed', { exact: true })).toBeVisible();
  await auditAccessibility(page, info, 'mixed-clock-import');
  const jobs = await (await page.request.get('/api/jobs')).json();
  expect(jobs[0]).toMatchObject({ state: 'completed', processed: 3, imported: 2, error: null });
  const library = await (await page.request.get('/api/games')).json();
  expect(library.total).toBe(2);
  let firstId = '';
  for (const item of library.items) {
    const game = await (await page.request.get(`/api/games/${item.id}`)).json();
    if (game.moves[0].uci === 'e2e4') {
      firstId = item.id; expect(game.moves[0].clockSeconds).toBeNull(); expect(game.rawPgn).toContain('999999999999999999999');
    } else expect(game.moves[0].clockSeconds).toBe(300);
  }
  expect(firstId).not.toBe('');
  await page.getByRole('link', { name: 'Open library →', exact: true }).click();
  await page.locator(`a.game-row[href="#/games/${firstId}"]`).click();
  await page.getByRole('button', { name: 'Move 1: e4', exact: true }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('Clock unavailable for this move.');
  await auditAccessibility(page, info, 'unavailable-optional-clock');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  await page.screenshot({ path: `test-results/mixed-clock-${info.project.name}.png`, fullPage: true });
});

test('exhausted PGN import gives a resubmission step (labelled browser response fixture)', async ({ page }, info) => {
  await register(page, `stopped-import-${info.project.name}`);
  // The real processor's queued/fourth-attempt boundary and legacy HTTP projection
  // have PostgreSQL integration checks. This fixture checks the rendered recovery action.
  await page.route('**/api/jobs', route => route.fulfill({ contentType: 'application/json', body: JSON.stringify([{
    id: '11111111-2222-4333-8444-666666666666', source: 'pgn', state: 'failed', phase: 'validating',
    imported: 0, duplicates: 0, processed: 0, total: 0, failures: [], version: 8, cancelRequested: false,
    nextAttemptAt: null, request: null,
    error: 'Import stopped after four interrupted attempts. Saved games remain available. Submit your PGN again to retry.',
  }]) }));
  await page.reload();
  await expect(page.getByText('failed', { exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('Submit your PGN again to retry.');
  await expect(page.getByText(/Import interrupted\. Retrying/)).toHaveCount(0);
  await page.getByLabel('Or paste PGN').fill('1.e4 e5 1/2-1/2');
  await expect(page.getByRole('button', { name: 'Import games', exact: true })).toBeEnabled();
  await auditAccessibility(page, info, 'exhausted-pgn-recovery');
  await page.screenshot({ path: `test-results/exhausted-pgn-${info.project.name}.png`, fullPage: true });
});
