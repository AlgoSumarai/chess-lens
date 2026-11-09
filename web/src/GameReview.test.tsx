import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { GameReview } from './GameReview';
import { api, send } from './api';
import { Chess } from 'chess.js';

const fixture = vi.hoisted(() => ({
  id: 'game-1', white: 'Learner', black: 'Opponent', result: '1-0', playerColour: 'white', initialFen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
  moves: [{ ply: 1, san: 'e4', uci: 'e2e4', side: 'white', fenBefore: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', fenAfter: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1', clockSeconds: null }], rawPgn: '1.e4 1-0',
}));
const remote = vi.hoisted(() => ({ loading: false }));
vi.mock('./hooks', () => ({ useRemote: () => ({ data: remote.loading ? undefined : fixture, reload: vi.fn() }) }));
vi.mock('./api', () => ({ api: vi.fn(), send: vi.fn() }));
vi.mock('./analysis', () => ({ useAnalysis: () => ({ data: null, error: '', connected: false, reload: vi.fn() }) }));
vi.mock('react-chessboard', async importOriginal => ({ ...(await importOriginal<typeof import('react-chessboard')>()), Chessboard: ({ options }: { options: { position: string } }) => <div data-testid="board-position">{options.position}</div> }));
afterEach(() => { cleanup(); remote.loading = false; vi.clearAllMocks(); vi.unstubAllGlobals(); });

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


test('refreshing game data keeps explored lines and the analysis toggle within the open review', async () => {
  vi.stubGlobal('matchMedia', () => ({ matches: true }));
  vi.mocked(send).mockResolvedValue(undefined);
  vi.mocked(api).mockImplementation((_path, options) => {
    const { ply, variation } = JSON.parse(options!.body as string) as { ply: number; variation: string[] };
    const board = new Chess(ply ? fixture.moves[0].fenAfter : fixture.initialFen);
    for (const uci of variation) board.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] });
    return Promise.resolve({ fen: board.fen(), sideToMove: board.turn() === 'w' ? 'white' : 'black',
      legalMoves: board.moves({ verbose: true }).map(m => m.lan), endgame: null });
  });
  const view = render(<GameReview id="game-1" />);
  await screen.findByText('The starting position of your game.');
  fireEvent.change(screen.getByLabelText('Coordinate move'), { target: { value: 'd2d4' } });
  fireEvent.click(screen.getByRole('button', { name: 'Play move' }));
  await screen.findByText(/Exploring an alternative line/);
  fireEvent.click(screen.getByRole('button', { name: 'Analyse this position' }));
  remote.loading = true;
  view.rerender(<GameReview id="game-1" />);
  expect(screen.getByRole('button', { name: 'Select variation 1' })).toBeVisible();
  expect(screen.getByRole('button', { name: 'Stop position analysis' })).toBeVisible();
  remote.loading = false;
  view.rerender(<GameReview id="game-1" />);
  expect(screen.getByRole('button', { name: 'Select variation 1' })).toHaveAttribute('aria-pressed', 'true');
  expect(screen.getByTestId('board-position')).toHaveTextContent('3P4');
});
