import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { PositionAnalysisPanel } from './PositionAnalysisPanel';
import { api, send } from './api';

vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
afterEach(() => { vi.useRealTimers(); vi.clearAllMocks(); });

test('a late candidate response stays hidden after navigation and stopping clears guidance', async () => {
  vi.useFakeTimers();
  const start = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  const afterE4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
  const requests: { sequence: number; resolve: (value: unknown) => void }[] = [];
  vi.mocked(send).mockImplementation((_path, data) => {
    const input = data as { cancelOnly?: boolean; sequence: number };
    return input.cancelOnly ? Promise.resolve(undefined) : new Promise(resolve => requests.push({ sequence: input.sequence, resolve }));
  });
  vi.mocked(api).mockImplementation(() => new Promise(() => {}));
  const view = render(<PositionAnalysisPanel gameId="game" ply={0} variation={[]} fen={start} ready revision={0} />);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(350); });
  view.rerender(<PositionAnalysisPanel gameId="game" ply={1} variation={[]} fen={afterE4} ready revision={0} />);
  await act(async () => { await vi.advanceTimersByTimeAsync(350); });
  expect(requests).toHaveLength(2);
  const answer = (index: number, fen: string, move: string) => ({ id: `job-${index}`, fen, sequence: requests[index].sequence,
    state: 'completed', version: 3, error: null, result: { engine: 'Labelled test fixture', lines: [{ rank: 1, depth: 10, score: { kind: 'cp', value: 15 }, pv: [move], bound: false }] } });
  await act(async () => { requests[0].resolve(answer(0, start, 'd2d4')); });
  expect(screen.queryByLabelText('Candidate continuations')).toBeNull();
  await act(async () => { requests[1].resolve(answer(1, afterE4, 'e7e5')); });
  expect(screen.getByLabelText('Candidate continuations')).toHaveTextContent('for Black');
  expect(screen.getByLabelText('Candidate continuations')).toHaveTextContent('e5');
  fireEvent.click(screen.getByRole('button', { name: 'Stop position analysis' }));
  expect(screen.queryByLabelText('Candidate continuations')).toBeNull();
  expect(vi.mocked(send).mock.calls.some(([, data]) => (data as { cancelOnly?: boolean }).cancelOnly)).toBe(true);
});
