import { expect, test } from '@playwright/test';
import { auditAccessibility, checkCoordinateContrast } from './accessibility';

test('small screens, long names, keyboard and touch promotion preserve legal play and focus', async ({ page, isMobile }, info) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: isMobile ? 320 : 1440, height: isMobile ? 740 : 1050 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`controls-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.locator('#main')).toBeFocused();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible();
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/account/csrf')).json()).token };
  const player = 'LearnerWithAnIntentionallyLongUnbrokenNameForSmallScreens';
  const imported = await page.request.post('/api/imports/pgn', { headers, data: { pgn: `[Event "Original promotion access fixture ${Date.now()}"]\n[White "${player}"]\n[Black "OpponentWithAnIntentionallyLongUnbrokenNameForSmallScreens"]\n[Result "1/2-1/2"]\n[FEN "7k/P7/6K1/8/8/8/8/8 w - - 0 1"]\n1.a8=N 1/2-1/2`, playerName: player, maxGames: 1 } });
  expect(imported.ok()).toBe(true);
  const importId = (await imported.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/jobs/${importId}`)).json()).state, { timeout: 30000 }).toBe('completed');
  const game = (await (await page.request.get('/api/games')).json()).items[0];
  await page.getByRole('link', { name: 'Your games', exact: true }).click();
  await expect(page.locator('.game-row')).toContainText(player);
  await auditAccessibility(page, info, 'long-name-library');
  await page.locator('.game-row').click();
  const status = page.getByRole('status', { name: 'Position status' });
  await expect(status).toContainText('starting position');
  await page.getByText('Current pieces and legal moves', { exact: true }).click();
  await expect(page.locator('.position-text')).toContainText('pawn on a7');
  await expect(page.locator('.position-text')).toContainText('a7a8q');
  await auditAccessibility(page, info, 'position-text');
  await checkCoordinateContrast(page);
  const coordinate = page.getByLabel('Coordinate move', { exact: true });
  const choosePromotion = async () => {
    if (isMobile) {
      await page.locator('[data-square="a7"]').tap();
      await page.locator('[data-square="a8"]').tap();
    } else {
      await coordinate.focus(); await page.keyboard.type('a7a8'); await page.keyboard.press('Enter');
    }
    await expect(page.getByRole('button', { name: 'Queen', exact: true })).toBeFocused();
    for (const name of ['Queen', 'Rook', 'Bishop', 'Knight', 'Cancel promotion']) await expect(page.getByRole('button', { name, exact: true })).toBeVisible();
  };
  await choosePromotion();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('group', { name: 'Promote your pawn to' })).toHaveCount(0);
  if (!isMobile) await expect(coordinate).toBeFocused();
  await coordinate.fill('');
  await choosePromotion();
  await auditAccessibility(page, info, 'promotion-choices');
  if (isMobile) await page.getByRole('button', { name: 'Rook', exact: true }).tap();
  else { await page.keyboard.press('Tab'); await page.keyboard.press('Enter'); }
  await expect(status).toContainText('Exploring an alternative');
  await expect(page.locator('.position-text')).toContainText('rook on a8');
  await expect(page.getByRole('heading', { name: /Checkmate/i })).toBeVisible();
  await page.getByRole('button', { name: 'Return to mainline' }).click();
  await expect(status).toContainText('starting position');
  await page.getByRole('button', { name: 'Next move', exact: true }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(status).toContainText('After a8=N');
  await page.keyboard.press('Home');
  await expect(status).toContainText('starting position');
  const run = await page.request.post(`/api/games/${game.id}/analysis`, { headers, data: { profile: 'deep' } });
  expect(run.ok()).toBe(true); const runId = (await run.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/analysis/${runId}`)).json()).state, { timeout: 120000 }).toBe('completed');
  await page.goto(`/#/games/${game.id}?ply=1&run=${runId}`);
  await page.getByRole('button', { name: 'Play from before this mistake', exact: true }).click();
  const practiceStatus = page.getByRole('status', { name: 'Mistake practice status' });
  await expect(practiceStatus).toContainText('Your move.', { timeout: 30000 });
  if (isMobile) { await page.locator('[data-square="a7"]').tap(); await page.locator('[data-square="a8"]').tap(); }
  else { await page.getByLabel('Coordinate move', { exact: true }).focus(); await page.keyboard.type('a7a8'); await page.keyboard.press('Enter'); }
  await expect(page.getByRole('button', { name: 'Queen', exact: true })).toBeFocused();
  await auditAccessibility(page, info, 'practice-promotion');
  if (isMobile) await page.getByRole('button', { name: 'Rook', exact: true }).tap();
  else { await page.keyboard.press('Tab'); await page.keyboard.press('Enter'); }
  await expect(practiceStatus).toContainText('Objective met · unassisted attempt.', { timeout: 60000 });
  await expect(page.getByLabel('Practice notation')).toContainText('a8=R#');
  await page.screenshot({ path: `test-results/promotion-access-${info.project.name}.png`, fullPage: true });
  await auditAccessibility(page, info, 'promotion-complete');
  // Text scaling is a separate check; actual browser 200% zoom is inspected in the headed AT session.
  await page.addStyleTag({ content: ':root { font-size: 32px; }' });
  await auditAccessibility(page, info, '200-percent-text');
});

test('long saved notation and an unbroken original annotation remain readable at small sizes', async ({ page, isMobile }, info) => {
  await page.setViewportSize({ width: isMobile ? 320 : 1440, height: isMobile ? 740 : 1050 });
  await page.goto('/'); await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`notation-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!'); await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible();
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/account/csrf')).json()).token };
  const annotation = 'OriginalUnbrokenAnnotationForWidthInspection'.repeat(100);
  const response = await page.request.post('/api/imports/pgn', { headers, data: { pgn: `[Event "Original long notation fixture ${Date.now()}"]\n[White "Learner"]\n[Black "Opponent"]\n[Result "1-0"]\n1.e4 {${annotation}} e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3 d6 8.c3 O-O 9.h3 Nb8 10.d4 Nbd7 11.c4 c6 12.Nc3 Qc7 13.Be3 Bb7 14.Rc1 Rfe8 15.cxb5 axb5 16.Nxb5 1-0`, playerName: 'Learner', maxGames: 1 } });
  expect(response.ok()).toBe(true); const id = (await response.json()).id;
  await expect.poll(async () => (await (await page.request.get(`/api/jobs/${id}`)).json()).state, { timeout: 30000 }).toBe('completed');
  await page.getByRole('link', { name: 'Your games', exact: true }).click(); await page.locator('.game-row').click();
  await expect(page.getByLabel('Game notation').getByRole('button')).toHaveCount(31);
  await page.getByRole('button', { name: 'Last position' }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('After Nxb5');
  await page.getByText('Original PGN', { exact: true }).click(); await expect(page.locator('.source-pgn pre')).toContainText(annotation);
  await auditAccessibility(page, info, 'long-notation-open-pgn');
  await page.addStyleTag({ content: ':root { font-size: 32px; }' });
  await page.getByRole('button', { name: 'First position' }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('starting position');
  await auditAccessibility(page, info, 'long-notation-200-percent-text');
  await page.screenshot({ path: `test-results/long-notation-${info.project.name}.png`, fullPage: true });
});
