import { expect, test } from '@playwright/test';

test('real deep review supports conversion/endgame findings with exact saved decisions and coverage', async ({ page }, info) => {
  await page.goto('/'); await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill(`objective-${info.project.name}-${Date.now()}@test.example`);
  await page.getByLabel('Password', { exact: true }).fill('ChessLens.E2e2026!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'A little reflection. A better next game.' })).toBeVisible({ timeout: 15000 });
  await page.getByRole('link', { name: 'Import your first games' }).click();
  await page.getByLabel('Or paste PGN').fill(`[Event "Original real-engine objective fixture ${Date.now()}"]\n[Date "2026.10.01"]\n[White "Learner"]\n[Black "Opponent"]\n[FEN "4k3/pp6/5n2/8/8/8/PPPP4/4K2R w - - 0 1"]\n1.Kf1 Ng4 2.Rh2 Nxh2+ 0-1`);
  await page.getByLabel('Your player name').fill('Learner'); await page.getByRole('button', { name: 'Import games', exact: true }).click();
  await expect(page.getByText('1 imported · 0 already in library · 0 could not be imported')).toBeVisible({ timeout: 30000 });
  await page.getByRole('link', { name: 'Open library →' }).click(); await page.locator('.game-row').click();
  await page.getByRole('button', { name: 'Deep review', exact: true }).click();
  await expect(page.getByText('completed · deep review · 4/4 half-moves')).toBeVisible({ timeout: 150000 });
  await page.getByRole('link', { name: 'Weaknesses', exact: true }).click();
  await page.getByLabel('From (UTC)').fill('2026-10-01'); await page.getByLabel('To, exclusive (UTC)').fill('2026-11-01');
  await page.getByRole('button', { name: 'Find my patterns', exact: true }).click();
  await expect(page.locator('.weakness-list').getByRole('heading', { name: 'Keeping an advantage', exact: true })).toBeVisible({ timeout: 30000 });
  await expect(page.getByRole('heading', { name: 'Endgame decisions', exact: true })).toHaveCount(2); // Coverage and finding headings.
  const finding = page.locator('.weakness-list .panel').filter({ has: page.getByRole('heading', { name: 'Keeping an advantage', exact: true }) });
  await expect(finding).toContainText('Early signal'); await expect(finding).toContainText('1/1 eligible moves');
  await page.screenshot({ path: `test-results/objective-findings-${info.project.name}.png`, fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  await finding.getByText('Inspect 1 supporting positions').click();
  await finding.getByRole('link', { name: /Review move 2 · white/ }).click();
  await expect(page.getByRole('status', { name: 'Position status' })).toContainText('After Rh2');
  await expect(page.getByText('Viewing the saved analysis referenced by this evidence.')).toBeVisible();
});
