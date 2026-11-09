import { expect, test, type Page } from '@playwright/test';
import { Chess } from 'chess.js';
import type { PositionJob } from '../src/positionAnalysis';
import { auditAccessibility } from './accessibility';

async function reviewed(page: Page) {
  await expect(page.getByRole('status', { name: 'Position analysis status' })).toContainText('Position reviewed', { timeout: 30000 });
  await expect(page.locator('.live-evaluation-score')).toHaveText(/^[+−-]?(\d+\.\d|M\d+)$/);
}
const fenOnBoard = async (page: Page) => (await page.locator('.position-text .mono').last().textContent())!.replace('Position (FEN): ', '');

// Uses the real API, PostgreSQL and Stockfish worker; no engine responses are mocked.
test('live Stockfish analysis, nested branches, selected-line replay and review lifetime', async ({ page }, info) => {
  const failures: string[] = [];
  const completedJobs: PositionJob[] = [];
  page.on('response', async response => {
    if (/\/api\/(position-analysis\/|games\/[^/]+\/position-analysis)/.test(response.url()) && response.status() !== 204) {
      const job = await response.json().catch(() => null) as PositionJob | null;
      if (job?.state === 'completed') completedJobs.push(job);
    }
  });
  page.on('pageerror', error => failures.push(error.message));
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`live-review-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible();
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/account/csrf')).json()).token };
  const imported = await page.request.post('/api/imports/pgn', { headers, data: {
    pgn: `[Event "Live review navigation ${Date.now()}"]\n[White "Review Learner"]\n[Black "Review Opponent"]\n[Result "1/2-1/2"]\n1. e4 e5 2. Nf3 Nc6 3. Bc4 Nf6 4. d3 Be7 5. O-O d6 1/2-1/2`,
    playerName: 'Review Learner', maxGames: 1,
  } });
  expect(imported.ok()).toBe(true);
  const importId = (await imported.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/jobs/${importId}`)).json()).state, { timeout: 30000 }).toBe('completed');
  const game = (await (await page.request.get('/api/games')).json()).items[0];
  await page.getByRole('link', { name: 'Your games', exact: true }).click();
  await page.locator('.game-row').click();
  const reviewUrl = page.url();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('starting position');
  await expect(page.locator('.live-evaluation-score')).toHaveCount(0);
  await page.getByRole('button', { name: 'Analyse this position', exact: true }).click();
  await reviewed(page);
  await expect(page.getByRole('status', { name: 'Position analysis status' })).toContainText('Stockfish');
  await page.getByRole('button', { name: 'Move 1: e4', exact: true }).click();
  await reviewed(page);
  await expect(page.getByLabel('Candidate continuations')).toContainText('for Black');
  await page.getByRole('button', { name: 'Move 4: Nc6', exact: true }).click();
  await reviewed(page);
  const candidates = page.getByRole('button', { name: /^Play candidate/ });
  expect(await candidates.count()).toBeLessThanOrEqual(3);
  const labels = await candidates.evaluateAll(buttons => buttons.map(button => button.getAttribute('aria-label')!));
  const first = labels.find(label => !label.endsWith(': Bc4'))!;
  const firstSan = first.split(': ')[1];
  await page.getByRole('button', { name: first, exact: true }).click();
  await expect(page.getByRole('group', { name: 'Variation 1 moves', exact: true })).toContainText(firstSan);
  await reviewed(page);
  const sharedFen = await fenOnBoard(page);
  const continuation = await candidates.first().getAttribute('aria-label');
  await candidates.first().click();
  await reviewed(page);
  const firstEndFen = await fenOnBoard(page);
  await page.getByRole('button', { name: 'Previous move', exact: true }).click();
  await reviewed(page);
  expect(await fenOnBoard(page)).toBe(sharedFen);
  const nextLabels = await candidates.evaluateAll(buttons => buttons.map(button => button.getAttribute('aria-label')!));
  const different = nextLabels.find(label => label.split(': ')[1] !== continuation!.split(': ')[1])!;
  await page.getByRole('button', { name: different, exact: true }).click();
  await reviewed(page);
  const secondEndFen = await fenOnBoard(page);
  expect(secondEndFen).not.toBe(firstEndFen);
  await expect(page.getByRole('button', { name: /^Select variation/ })).toHaveCount(2);
  await expect(page.getByRole('button', { name: 'Select variation 2', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Previous move', exact: true }).click();
  await expect.poll(() => fenOnBoard(page)).toBe(sharedFen);
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await expect.poll(() => fenOnBoard(page)).toBe(secondEndFen);
  await page.getByRole('button', { name: 'Previous move', exact: true }).click();
  await page.getByRole('button', { name: 'Select variation 1', exact: true }).click();
  await expect.poll(() => fenOnBoard(page)).toBe(sharedFen);
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await expect.poll(() => fenOnBoard(page)).toBe(firstEndFen);
  await page.getByRole('button', { name: 'First position', exact: true }).click();
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await expect.poll(() => fenOnBoard(page)).toBe(sharedFen);
  await reviewed(page);
  // Replaying an existing candidate follows the tracked line without duplication.
  const existingLabel = (await candidates.evaluateAll(buttons => buttons.map(button => button.getAttribute('aria-label')!))).find(label => label.split(': ')[1] === continuation!.split(': ')[1])!;
  await page.getByRole('button', { name: existingLabel, exact: true }).click();
  await expect.poll(() => fenOnBoard(page)).toBe(firstEndFen);
  await expect(page.getByRole('button', { name: /^Select variation/ })).toHaveCount(2);
  await page.getByRole('button', { name: 'Move 4: Nc6', exact: true }).click();
  await page.getByRole('button', { name: 'Next move', exact: true }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('After Bc4');
  await expect(page.getByRole('group', { name: 'Game notation', exact: true }).getByRole('button')).toHaveCount(10);

  // Delay real responses during rapid navigation, then validate every SAN move against the final board.
  await page.route('**/api/position-analysis/*', async route => { const response = await route.fetch(); await new Promise(resolve => setTimeout(resolve, 250)); await route.fulfill({ response }); });
  for (const move of ['Move 1: e4', 'Move 4: Nc6', 'Move 2: e5', 'Move 7: d3', 'Move 3: Nf3']) await page.getByRole('button', { name: move, exact: true }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('After Nf3');
  await reviewed(page);
  const finalFen = await fenOnBoard(page);
  await expect.poll(() => completedJobs.some(job => job.fen === finalFen)).toBe(true);
  const finalJob = completedJobs.filter(job => job.fen === finalFen).at(-1)!;
  const bestScore = [...finalJob.result!.lines].sort((a, b) => a.rank - b.rank)[0].score;
  const value = finalFen.split(' ')[1] === 'w' ? bestScore.value : -bestScore.value;
  const expectedScore = bestScore.kind === 'mate' ? `${value < 0 ? '−' : ''}M${Math.abs(value)}` : `${value >= 0 ? '+' : ''}${(value / 100).toFixed(1)}`;
  await expect(page.locator('.live-evaluation-score')).toHaveText(expectedScore);
  for (const text of await page.locator('.candidate .variation-line').allTextContents()) {
    const board = new Chess(finalFen);
    const moves = text.trim().split(/\s+/);
    expect(moves.length).toBeLessThanOrEqual(10);
    for (const move of moves) expect(board.move(move)).toBeTruthy();
  }
  const completed = (await page.getByLabel('Candidate continuations').getByRole('button').first().textContent())!;
  expect(completed).toContain('for Black');
  await page.getByRole('button', { name: 'Flip board' }).click();
  await expect(page.locator('.live-evaluation-bar')).toHaveClass(/black/);
  const bar = await page.locator('.live-evaluation-bar').boundingBox();
  const board = await page.locator('.board-wrap').boundingBox();
  expect(Math.abs(bar!.height - board!.height)).toBeLessThanOrEqual(1);
  expect(Math.abs(bar!.y - board!.y)).toBeLessThanOrEqual(1);
  expect(bar!.x + bar!.width).toBeLessThan(board!.x);
  await auditAccessibility(page, info, 'live-review-branches');
  await page.screenshot({ path: `test-results/live-review-${info.project.name}.png`, fullPage: true });
  await page.getByRole('button', { name: 'Stop position analysis', exact: true }).click();
  await expect(page.locator('.live-evaluation-score')).toHaveCount(0);
  await expect(page.getByLabel('Candidate continuations')).toHaveCount(0);
  await page.getByRole('link', { name: '← Your games', exact: true }).click();
  await page.goto(reviewUrl);
  await expect(page.getByRole('button', { name: /^Select variation/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Analyse this position', exact: true })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Game notation', exact: true }).getByRole('button')).toHaveCount(10);
  expect((await (await page.request.get(`/api/games/${game.id}`)).json()).moves).toHaveLength(10);
  expect(failures).toEqual([]);
});
