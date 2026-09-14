import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { GameReview } from './GameReview';
import { api } from './api';

const fixture = vi.hoisted(() => ({
  id: 'game-1', white: 'Learner', black: 'Opponent', result: '1-0', playerColour: 'white', initialFen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
  moves: [{ ply: 1, san: 'e4', uci: 'e2e4', side: 'white', fenBefore: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', fenAfter: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1', clockSeconds: null }], rawPgn: '1.e4 1-0',
}));
vi.mock('./hooks', () => ({ useRemote: () => ({ data: fixture, reload: vi.fn() }) }));
vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
vi.mock('./analysis', () => ({ useAnalysis: () => ({ data: null, error: '', connected: false, reload: vi.fn() }) }));
vi.mock('react-chessboard', () => ({ Chessboard: ({ options }: { options: { position: string } }) => <div data-testid="board-position">{options.position}</div> }));
afterEach(() => vi.clearAllMocks());

test('late server replies do not replace the selected notation, board or explanation', async () => {
  vi.stubGlobal('matchMedia', () => ({ matches: true }));
  const replies: ((value: unknown) => void)[] = [];
  vi.mocked(api).mockImplementation(() => new Promise(resolve => replies.push(resolve)));
  render(<GameReview id="game-1" />);
  await waitFor(() => expect(replies.length).toBe(1));
  fireEvent.click(screen.getByRole('button', { name: 'Next move' }));
  await waitFor(() => expect(replies.length).toBe(2));
  replies[1]({ fen: fixture.moves[0].fenAfter, legalMoves: ['e7e5'], sideToMove: 'black', endgame: null });
  await screen.findByText(/After e4/);
  replies[0]({ fen: fixture.initialFen, legalMoves: ['e2e4'], sideToMove: 'white', endgame: null });
  await waitFor(() => expect(screen.getByTestId('board-position')).toHaveTextContent(fixture.moves[0].fenAfter));
  expect(screen.getByRole('button', { name: 'Move 1: e4' })).toHaveAttribute('aria-current', 'step');
  expect(screen.getByRole('status', { name: 'Position status' })).toHaveTextContent('After e4');
});
