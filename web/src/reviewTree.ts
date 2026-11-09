import { Chess } from 'chess.js';
import type { Game } from './api';

export type ReviewNode = {
  id: string; parent?: string; children: string[]; fen: string; uci?: string; san?: string;
  // The server replays the imported game up to ply, then the entire variation.
  ply: number; variation: string[];
};
export type ReviewTree = {
  nodes: Record<string, ReviewNode>; current: string; line: string; mainline: string[]; nextId: number;
};
type Action = { type: 'navigate'; direction: 'first' | 'previous' | 'next' | 'last' }
  | { type: 'select'; node: string; line: string }
  | { type: 'line'; line: string }
  | { type: 'play'; uci: string };

export function createReviewTree({ game, initialPly }: { game: Game; initialPly: number }): ReviewTree {
  const root: ReviewNode = { id: 'main-0', children: [], fen: game.initialFen, ply: 0, variation: [] };
  const nodes: ReviewTree['nodes'] = { [root.id]: root };
  const mainline = [root.id];
  for (const move of game.moves) {
    const parent = mainline[mainline.length - 1];
    const id = `main-${move.ply}`;
    nodes[parent].children.push(id);
    nodes[id] = { id, parent, children: [], fen: move.fenAfter, uci: move.uci, san: move.san, ply: move.ply, variation: [] };
    mainline.push(id);
  }
  const ply = Number.isInteger(initialPly) ? Math.max(0, Math.min(initialPly, game.moves.length)) : 0;
  return { nodes, mainline, current: mainline[ply], line: mainline[mainline.length - 1], nextId: 1 };
}

export function reviewPath(tree: ReviewTree, end: string): ReviewNode[] {
  const path: ReviewNode[] = [];
  let node: ReviewNode | undefined = tree.nodes[end];
  while (node) { path.push(node); node = node.parent ? tree.nodes[node.parent] : undefined; }
  return path.reverse();
}

export function reviewVariations(tree: ReviewTree): ReviewNode[] {
  // Traverse children in exploration order, so extending a line keeps its label
  // in the same place even when a sibling continuation was created later.
  const ends: ReviewNode[] = [];
  const pending = [tree.mainline[0]];
  while (pending.length) {
    const node = tree.nodes[pending.pop()!];
    if (!node.children.length && node.variation.length) ends.push(node);
    pending.push(...node.children.slice().reverse());
  }
  return ends;
}

export function reviewReducer(tree: ReviewTree, action: Action): ReviewTree {
  if (action.type === 'select') return { ...tree, current: action.node, line: action.line };
  if (action.type === 'line') {
    const path = reviewPath(tree, action.line);
    return { ...tree, line: action.line, current: path.some(n => n.id === tree.current) ? tree.current : action.line };
  }
  if (action.type === 'navigate') {
    const path = reviewPath(tree, tree.line);
    const index = path.findIndex(n => n.id === tree.current);
    const next = action.direction === 'first' ? 0 : action.direction === 'last' ? path.length - 1
      : Math.max(0, Math.min(path.length - 1, index + (action.direction === 'next' ? 1 : -1)));
    return { ...tree, current: path[next].id };
  }
  const parent = tree.nodes[tree.current];
  const existing = parent.children.find(id => tree.nodes[id].uci === action.uci);
  if (existing) {
    // Keep the selected continuation at a shared ancestor whenever possible.
    let end = tree.line;
    if (!reviewPath(tree, end).some(n => n.id === existing)) {
      end = existing;
      while (tree.nodes[end].children.length) end = tree.nodes[end].children[0];
    }
    return { ...tree, current: existing, line: end };
  }
  if (parent.variation.length >= 100) return tree;
  const board = new Chess(parent.fen);
  const move = board.move({ from: action.uci.slice(0, 2), to: action.uci.slice(2, 4), promotion: action.uci[4] });
  const id = `variation-${tree.nextId}`;
  const node: ReviewNode = { id, parent: parent.id, children: [], fen: board.fen(), uci: action.uci,
    san: move.san, ply: parent.ply, variation: [...parent.variation, action.uci] };
  return { ...tree, nodes: { ...tree.nodes, [parent.id]: { ...parent, children: [...parent.children, id] }, [id]: node },
    current: id, line: id, nextId: tree.nextId + 1 };
}

export function moveNumber(fenBefore: string) {
  const fields = fenBefore.split(' ');
  return `${fields[5]}${fields[1] === 'w' ? '.' : '…'}`;
}
