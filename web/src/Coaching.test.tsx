import { act, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { Coaching } from './Coaching';
import { api, send } from './api';

vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
afterEach(() => { vi.clearAllMocks(); });

const answer = (ply: number, fen: string, level = 'beginner') => ({ id: `coach-${ply}-${level}`, runId: 'run', ply, state: 'completed', version: 2, error: null,
  result: { evidenceId: `${ply}-${level}`, runId: 'run', ply, fen, level, rating: { provider: 'pgn', pool: null, timeControl: null, rating: null, presentationAdjusted: false },
    whatHappened: `Verified decision ${ply}`, whyItMattered: 'Verified evaluation change', betterLine: ['e2e4'], sanLine: ['e4'], lesson: `A ${level} lesson`,
    mode: 'factual', fallbackReason: null, promptVersion: 'labelled-fixture-v1', model: null } });

test('late coaching cannot replace another decision and changing levels hides the old explanation immediately', async () => {
  const requests: { resolve: (value: unknown) => void }[] = [];
  vi.mocked(api).mockImplementation(() => new Promise(resolve => requests.push({ resolve })));
  vi.mocked(send).mockResolvedValue(undefined);
  const view = render(<Coaching runId="run" ply={1} fen="first" level="beginner" modelOptIn={false} />);
  await waitFor(() => expect(requests).toHaveLength(1));
  view.rerender(<Coaching runId="run" ply={3} fen="third" level="beginner" modelOptIn={false} />);
  await waitFor(() => expect(requests).toHaveLength(2));
  await act(async () => requests[0].resolve(answer(1, 'first')));
  expect(screen.queryByText(/Verified decision 1/)).toBeNull();
  await act(async () => requests[1].resolve(answer(3, 'third')));
  expect(screen.getByText(/Verified decision 3/)).toBeVisible();
  view.rerender(<Coaching runId="run" ply={3} fen="third" level="advanced" modelOptIn={false} />);
  expect(screen.queryByText(/Verified decision 3/)).toBeNull();
  await waitFor(() => expect(requests).toHaveLength(3));
  await act(async () => requests[2].resolve(answer(3, 'third', 'advanced')));
  expect(screen.getByText(/A advanced lesson/)).toBeVisible();
});

test('an unrelated server explanation is rejected instead of showing guidance for a different position', async () => {
  vi.mocked(api).mockResolvedValue(answer(7, 'different'));
  vi.mocked(send).mockResolvedValue(undefined);
  render(<Coaching runId="run" ply={3} fen="third" level="beginner" modelOptIn={false} />);
  await screen.findByText('This explanation does not match the selected decision.');
  expect(screen.queryByText(/Verified decision 7/)).toBeNull();
});
