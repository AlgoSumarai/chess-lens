import { useMemo } from 'react';
import { Chess } from 'chess.js';

const pieces = { k: 'king', q: 'queen', r: 'rook', b: 'bishop', n: 'knight', p: 'pawn' };

/** The same displayed position in words, without an evaluation or suggested move. */
export function BoardPositionText({ fen, legalMoves }: { fen: string; legalMoves?: string[] }) {
  const board = useMemo(() => new Chess(fen), [fen]);
  const occupied = board.board().flat().filter(piece => piece !== null);
  return <details className="position-text">
    <summary>Current pieces and legal moves</summary>
    <p>{board.turn() === 'w' ? 'White' : 'Black'} to move.{board.isCheck() ? ' In check.' : ''}</p>
    {(['w', 'b'] as const).map(colour => <p key={colour}><strong>{colour === 'w' ? 'White' : 'Black'}:</strong>{' '}
      {occupied.filter(piece => piece.color === colour).map(piece => `${pieces[piece.type]} on ${piece.square}`).join('; ')}.
    </p>)}
    {legalMoves && <p>Legal coordinate moves: <span className="mono">{legalMoves.join(' · ') || 'No legal moves.'}</span></p>}
    <p>To enter a move, use its starting square and destination, such as e2e4. For promotion, add q, r, b or n, or enter the two squares to choose a piece.</p>
    <p className="mono">Position (FEN): {fen}</p>
  </details>;
}
