import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { PracticePage } from './Practice';
import { send } from './api';

const mocks = vi.hoisted(() => ({ session: {} as Record<string, unknown>, reload: vi.fn() }));
vi.mock('./jobHooks', () => ({ useSavedJob: () => ({ data: mocks.session, error: '', reload: mocks.reload, connected: true }) }));
vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
vi.mock('react-chessboard', async importOriginal => ({ ...(await importOriginal<typeof import('react-chessboard')>()), Chessboard: () => <div aria-label="Labelled board component fixture" /> }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
beforeEach(() => {
  vi.clearAllMocks(); vi.stubGlobal('matchMedia', () => ({ matches: true }));
  mocks.session = { id: 'session', state: 'active', version: 2, side: 'white', objective: { title: 'Improve decision safety', instruction: 'Three decisions', decisions: 3 }, error: null, opponent: 'Labelled practical opponent', attempts: [],
    attempt: { id: 'attempt', number: 1, objective: { title: 'Improve decision safety', instruction: 'Three decisions', decisions: 3 }, state: 'active', hintsUsed: 0, undosUsed: 0, assisted: false, initialFen: '7k/P7/6K1/8/8/8/8/8 w - - 0 1', fen: '7k/P7/6K1/8/8/8/8/8 w - - 0 1', endgame: null, history: [], notation: [], legalMoves: ['a7a8q', 'a7a8r', 'a7a8b', 'a7a8n'], hint: null, decisionsPlayed: 0, review: null } };
  vi.mocked(send).mockResolvedValue({ ...mocks.session, state: 'waiting', version: 3 });
});

test('coordinate promotion offers all pieces and submits the exact version and attempt once', async () => {
  render(<PracticePage id="session" />);
  fireEvent.change(screen.getByLabelText('Coordinate move'), { target: { value: 'a7a8' } }); fireEvent.click(screen.getByRole('button', { name: 'Play move' }));
  for (const piece of ['Queen', 'Rook', 'Bishop', 'Knight']) expect(screen.getByRole('button', { name: piece })).toBeVisible();
  fireEvent.click(screen.getByRole('button', { name: 'Knight' }));
  await waitFor(() => expect(send).toHaveBeenCalledWith('/practice/session/moves', { version: 2, attemptId: 'attempt', move: 'a7a8n' }));
  expect(send).toHaveBeenCalledTimes(1);
});

test('pending work hides move entry and grades while preserving undo and cancellation', () => {
  const attempt = mocks.session.attempt as Record<string, unknown>; attempt.history = ['a7a8n']; attempt.notation = ['a8=N']; attempt.fen = 'N6k/8/6K1/8/8/8/8/8 b - - 0 1';
  mocks.session.state = 'waiting'; render(<PracticePage id="session" />);
  expect(screen.queryByLabelText('Coordinate move')).toBeNull(); expect(screen.queryByText('Review the attempt')).toBeNull();
  expect(screen.getByRole('button', { name: 'Get a hint' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Undo last decision' })).toBeEnabled(); expect(screen.getByRole('button', { name: 'End attempt' })).toBeEnabled();
});
