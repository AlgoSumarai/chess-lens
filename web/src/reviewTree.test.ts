import { expect, test } from 'vitest';
import { Chess } from 'chess.js';
import type { Game, Move } from './api';
import { createReviewTree, moveNumber, reviewPath, reviewReducer, reviewVariations } from './reviewTree';

function gameFrom(sans: string[], initialFen = new Chess().fen()): Game {
  const board = new Chess(initialFen);
  const moves: Move[] = sans.map((san, index) => {
    const fenBefore = board.fen();
    const move = board.move(san);
    return { ply: index + 1, san: move.san, uci: move.lan, side: move.color === 'w' ? 'white' : 'black', fenBefore, fenAfter: board.fen(), clockSeconds: null };
  });
  return { id: 'game', initialFen, moves } as Game;
}

test('nested branches preserve both continuations and selected-line navigation at every shared ancestor', () => {
  const game = gameFrom(['e4', 'e5', 'Nf3', 'Nc6']);
  const original = JSON.stringify(game);
  let tree = createReviewTree({ game, initialPly: 1 });
  for (const uci of ['c7c5', 'g1f3', 'b8c6']) tree = reviewReducer(tree, { type: 'play', uci });
  const firstLine = tree.line;
  tree = reviewReducer(tree, { type: 'navigate', direction: 'previous' });
  tree = reviewReducer(tree, { type: 'play', uci: 'd7d6' });
  const secondLine = tree.line;
  expect(tree.nodes[firstLine].san).toBe('Nc6');
  expect(tree.nodes[secondLine].san).toBe('d6');
  for (let index = 0; index < 3; index++) tree = reviewReducer(tree, { type: 'navigate', direction: 'previous' });
  expect(tree.current).toBe('main-1');
  for (let index = 0; index < 3; index++) tree = reviewReducer(tree, { type: 'navigate', direction: 'next' });
  expect(tree.current).toBe(secondLine);
  tree = reviewReducer(tree, { type: 'select', node: 'main-1', line: firstLine });
  tree = reviewReducer(tree, { type: 'navigate', direction: 'last' });
  expect(tree.current).toBe(firstLine);
  tree = reviewReducer(tree, { type: 'navigate', direction: 'first' });
  expect(tree.current).toBe('main-0');
  tree = reviewReducer(tree, { type: 'navigate', direction: 'last' });
  expect(tree.current).toBe(firstLine);
  expect(reviewPath(tree, secondLine).map(n => n.uci).filter(Boolean)).toEqual(['e2e4', 'c7c5', 'g1f3', 'd7d6']);
  expect(tree.nodes[secondLine].ply).toBe(1);
  expect(tree.nodes[secondLine].variation).toEqual(['c7c5', 'g1f3', 'd7d6']);
  expect(JSON.stringify(game)).toBe(original);
});

test('replaying an existing move follows its continuation without duplicate branches or losing the chosen line', () => {
  const game = gameFrom(['e4', 'e5', 'Nf3']);
  let tree = createReviewTree({ game, initialPly: 0 });
  tree = reviewReducer(tree, { type: 'play', uci: 'e2e4' });
  expect(tree.current).toBe('main-1');
  expect(tree.line).toBe('main-3');
  tree = reviewReducer(tree, { type: 'play', uci: 'c7c5' });
  tree = reviewReducer(tree, { type: 'play', uci: 'g1f3' });
  const chosen = tree.line;
  const count = Object.keys(tree.nodes).length;
  tree = reviewReducer(tree, { type: 'navigate', direction: 'first' });
  tree = reviewReducer(tree, { type: 'play', uci: 'e2e4' });
  tree = reviewReducer(tree, { type: 'play', uci: 'c7c5' });
  expect(tree.line).toBe(chosen);
  expect(Object.keys(tree.nodes)).toHaveLength(count);
  tree = reviewReducer(tree, { type: 'line', line: 'main-3' });
  expect(tree.current).toBe('main-3');
  expect(createReviewTree({ game, initialPly: 0 }).nodes).not.toHaveProperty(chosen);
});

test('custom starting positions retain correct black move numbers and promotion SAN', () => {
  const game = gameFrom([], '8/8/8/8/8/6k1/p7/4K3 b - - 0 17');
  let tree = createReviewTree({ game, initialPly: 0 });
  expect(moveNumber(tree.nodes[tree.current].fen)).toBe('17…');
  tree = reviewReducer(tree, { type: 'play', uci: 'a2a1q' });
  expect(tree.nodes[tree.current].san).toBe('a1=Q+');
});


test('extending an earlier branch keeps variation ordering stable', () => {
  let tree = createReviewTree({ game: gameFrom(['e4', 'e5']), initialPly: 1 });
  tree = reviewReducer(tree, { type: 'play', uci: 'c7c5' });
  const first = tree.current;
  tree = reviewReducer(tree, { type: 'navigate', direction: 'previous' });
  tree = reviewReducer(tree, { type: 'play', uci: 'e7e6' });
  const second = tree.current;
  tree = reviewReducer(tree, { type: 'select', node: first, line: first });
  tree = reviewReducer(tree, { type: 'play', uci: 'g1f3' });
  expect(reviewVariations(tree).map(n => n.id)).toEqual([tree.current, second]);
});
