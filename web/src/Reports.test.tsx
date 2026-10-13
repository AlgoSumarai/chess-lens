import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { ProgressPage, ReportPage, ReportsLibrary } from './Reports';
import { send } from './api';

const mocks = vi.hoisted(() => ({ report: {} as Record<string, unknown>, reload: vi.fn() }));
vi.mock('./jobHooks', () => ({ useSavedJob: () => ({ data: mocks.report, error: '', reload: mocks.reload, connected: false }) }));
vi.mock('./hooks', () => ({ useRemote: (path: string) => ({ data: path === '/reports/calendar' ? { timezone: 'Africa/Johannesburg', latestCompleted: { fromDate: '2026-10-05', toDate: '2026-10-12' } } : { total: 0, items: [] }, error: '', reload: mocks.reload }) }));
vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
afterEach(cleanup);
beforeEach(() => { vi.clearAllMocks(); location.hash = '/'; });

test('unknown opening is an explicit filter and dates are sent without browser timezone conversion', async () => {
  vi.mocked(send).mockResolvedValue({ id: 'saved-report' }); render(<ProgressPage />);
  fireEvent.change(screen.getByLabelText('Games played from (UTC)'), { target: { value: '2026-10-01' } });
  fireEvent.change(screen.getByLabelText('Through, inclusive (UTC)'), { target: { value: '2026-10-13' } });
  fireEvent.change(screen.getByLabelText('Exact opening name'), { target: { value: 'Unsupported inference' } });
  fireEvent.click(screen.getByLabelText('Only unknown openings'));
  expect(screen.getByLabelText('Exact opening name')).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'View saved progress' }));
  await waitFor(() => expect(send).toHaveBeenCalledWith('/progress', expect.objectContaining({ fromDate: '2026-10-01', throughDate: '2026-10-13', opening: null, unknownOpening: true })));
  expect(location.hash).toBe('#/reports/saved-report');
});

test('an empty completed week explains the missing activity and offers progress', async () => {
  vi.mocked(send).mockResolvedValue({ state: 'empty', period: { fromDate: '2026-10-05', toDate: '2026-10-12' }, detail: 'No new completed review activity.' });
  render(<ReportsLibrary />); fireEvent.click(screen.getByRole('button', { name: 'Generate saved report' }));
  expect(await screen.findByRole('status')).toHaveTextContent('No new completed review activity.');
  expect(screen.getByRole('link', { name: 'View your progress' })).toHaveAttribute('href', '#/progress');
});

test('supported labelled comparison shows both denominators and an accessible descriptive chart', () => {
  const comparison = { status: 'supported', explanation: 'A labelled descriptive fixture, not proof of a training effect.', currentErrors: 2, currentEligible: 20, previousErrors: 4, previousEligible: 20, rateDifference: -.1 };
  const coverage = { selectedGames: 5, reviewedGames: 5, playerDecisions: 20, completeDecisions: 20, stableDecisions: 20, provisionalDecisions: 0, rejectedDecisions: 0, missingIdentityGames: 0, unreviewedGames: 0, undatedGames: 0, selectionCapped: false, contextGames: 5, contextReviewedGames: 5 };
  mocks.report = { id: 'saved', kind: 'progress', revision: 1, state: 'completed', version: 2, error: null, period: { fromDate: '2026-10-05', toDate: '2026-10-12', timezone: 'UTC' }, previousPeriod: { fromDate: '2026-09-28', toDate: '2026-10-05' }, capturedAt: '2026-10-13T00:00:00Z', rulesVersion: 'Labelled rules fixture', detectorVersion: 'Labelled detector fixture', result: { matchingGames: 5, coverage, results: { wins: 0, draws: 0, losses: 5, unknown: 0 }, training: [], trainingCount: 0, trainingCapped: false, practicePlan: ['Inspect the evidence.'], duePuzzles: [], undatedGamesExcluded: 0, groups: [{ identity: 'fixture', source: 'pgn', ratingPool: 'unknown', colour: 'white', opening: 'Opening unknown', timeControlGroup: 'blitz', exactTimeControls: ['300+0'], rated: null, analysis: { profile: 'deep', engine: 'Labelled engine fixture' }, coverage, comparison, summary: { verifiedErrorMoves: 2, eligibleMoves: 20, clockMoves: 0, clockCoverage: 0, clockAvailable: false, findings: [] }, weaknessRates: [], ratings: [] }] } };
  render(<ReportPage id="saved" />);
  expect(screen.getByText('Previous: 4/20 · 20.0% · Current: 2/20 · 10.0%.')).toBeVisible();
  expect(screen.getByRole('img', { name: /previous 20.0 percent, current 10.0 percent/ })).toBeVisible();
  expect(screen.getByText(/labelled descriptive fixture/)).toBeVisible();
  expect(screen.queryByText(/5 attempts finished/)).not.toBeInTheDocument();
});
