import { defaultPieces, type PieceRenderObject } from 'react-chessboard';

const names: Record<string, string> = { K: 'king', Q: 'queen', R: 'rook', B: 'bishop', N: 'knight', P: 'pawn' };

// The library's draggable buttons obtain their accessible names from these images.
// Preserve its artwork and drag behavior; name both the piece and its current square.
export const namedPieces: PieceRenderObject = Object.fromEntries(Object.entries(defaultPieces).map(([code, Piece]) => [
  code, props => <span role="img" aria-label={`${code[0] === 'w' ? 'White' : 'Black'} ${names[code[1]]}${props?.square ? ` on ${props.square}` : ''}`} style={{ display: 'block', width: '100%', height: '100%' }}>
    <span aria-hidden="true"><Piece {...props} /></span>
  </span>,
]));
