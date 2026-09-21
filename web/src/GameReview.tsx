import { useEffect, useMemo, useState, type CSSProperties, type FormEvent } from 'react';
import { Chess } from 'chess.js';
import { Chessboard } from 'react-chessboard';
import { api, send, type Game, type Position } from './api';
import { useRemote } from './hooks';
import { ErrorNotice, Panel } from './App';
import { useAnalysis } from './analysis';
import { AnalysisPanel, EvaluationBar, EvaluationGraph } from './AnalysisPanel';
import { PositionAnalysisPanel } from './PositionAnalysisPanel';

export function GameReview({ id, initialPly = 0, runId }: { id: string; initialPly?: number; runId?: string }) {
  const { data: game, error, reload } = useRemote<Game>(`/games/${id}`);
  if (error) return <ErrorNotice message={error} retry={reload} />;
  if (!game) return <p aria-busy="true">Opening your game…</p>;
  return <>
    {runId && <Panel>
        <p>Viewing the saved analysis referenced by this evidence.</p>
        <a href={`#/games/${id}?ply=${initialPly}`}>Open the latest review</a>
      </Panel>}
    <Review game={game} reload={reload} initialPly={initialPly} runId={runId} />
    </>;
}

function Review({ game, reload, initialPly, runId }: { game: Game; reload: () => void; initialPly: number; runId?: string }) {
  const analysis = useAnalysis(game.id, runId);
  const [ply, setPly] = useState(Number.isInteger(initialPly) ? Math.max(0, Math.min(initialPly, game.moves.length)) : 0);
  const [variation, setVariation] = useState<string[]>([]);
  const [orientation, setOrientation] = useState<'white' | 'black'>(game.playerColour ?? 'white');
  const [selected, setSelected] = useState<string>();
  const [positionResponse, setPosition] = useState<Position & { requestKey: string }>();
  const requestKey = `${game.id}/${ply}/${variation.join(' ')}`;
  const position = positionResponse?.requestKey === requestKey ? positionResponse : undefined;
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [promotion, setPromotion] = useState<string>();
  const [typed, setTyped] = useState('');
  const mainlineFen = ply === 0 ? game.initialFen : game.moves[ply - 1].fenAfter;
  const local = useMemo(() => {
    
    const board = new Chess(mainlineFen);
    for (const uci of variation) 
      board.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] });
    return board;
  }, [mainlineFen, variation]);

  useEffect(() => {
    const controller = new AbortController();
    setBusy(true); setError(''); 
    setPosition(undefined);
    void api<Position>(`/games/${game.id}/position`, { method: 'POST', 
      body: JSON.stringify({ ply, variation }), 
      signal: controller.signal })
      .then(p => { if (!controller.signal.aborted) { setPosition({ ...p, requestKey }); setBusy(false); } })
      .catch(e => { if (!controller.signal.aborted) { setError(e.message); setBusy(false); } });
    return () => controller.abort();
  }, [game.id, ply, variation, requestKey]);

  const navigate = (next: number) => { setPly(Math.max(0, Math.min(game.moves.length, next))); 
    setVariation([]); 
    setSelected(undefined); 
    setPromotion(undefined); };
  
  const play = (uci: string) => {
    if (busy || !position?.legalMoves.includes(uci)) { 
      setError('Choose a legal move in the current position.'); 
      return false; 
    }
    setVariation(v => [...v, uci]); 
    setSelected(undefined); setPromotion(undefined); 
    setTyped(''); 
    
    return true;
  };

  const choose = (from: string, to: string) => {
    const matching = position?.legalMoves.filter(m => m.startsWith(from + to)) ?? [];
    if (matching.some(m => m.length === 5)) { 
      setPromotion(from + to); 
      return false; 
    }
    return matching.length === 1 && play(matching[0]);
  };
  const styles: Record<string, CSSProperties> = {};
  const last = variation.at(-1) ?? (ply > 0 ? game.moves[ply - 1].uci : undefined);
  if (last) { 
    styles[last.slice(0, 2)] = { backgroundColor: '#d2ba63' }; 
    styles[last.slice(2, 4)] = { backgroundColor: '#d2ba63' }; 
  }
  if (selected) {
    styles[selected] = { boxShadow: 'inset 0 0 0 4px #a76b17' };
    position?.legalMoves.filter(m => 
      m.startsWith(selected)).forEach(m => { 
        styles[m.slice(2, 4)] = { backgroundImage: 'radial-gradient(circle, #18233288 22%, transparent 25%)' }; 
      }
    );
  }
  const move = ply > 0 ? game.moves[ply - 1] : undefined;
  const moveAnalysis = analysis.data?.moves.find(m => m.ply === ply);
  const keyboardMove = (e: FormEvent) => { 
      e.preventDefault(); 
      const uci = typed.trim().toLowerCase(); 
      if (!play(uci) && uci.length === 4) 
        choose(uci.slice(0, 2), uci.slice(2, 4)); 
      };
  return <>
    <a className="back-link" href="#/games">← Your games</a>
    <div className="page-heading compact review-heading">
      <div>
        <p className="eyebrow">GAME REVIEW</p>
        <h1>{game.white} <span className="versus">vs</span> {game.black}</h1>
        <p>{game.opening ?? 'Opening unknown'} · {game.timeControl ?? 'Time control unavailable'}</p>
      </div>
      <span className="result large">{game.result}</span>
    </div>
    {!game.playerColour && <Panel className="identity">
        <div>
          <h3>Which player were you?</h3>
          <p>Select your colour before personal insights can be calculated.</p>
        </div>
        {(['white', 'black'] as const).map(c => 
          <button key={c} onClick={() => 
            void send(`/games/${game.id}/identity`, { colour: c }, 'PUT').then(reload).catch(e => setError(e.message))}>I played {c} · {c === 'white' ? game.white : game.black}</button>
          )}
      </Panel>
    }
    <div className="review-grid" onKeyDown={e => {
      if (['INPUT', 'SELECT', 'TEXTAREA'].includes((e.target as HTMLElement).tagName)) return;
      if (e.key === 'ArrowLeft') { 
        e.preventDefault(); 
        navigate(ply - 1); 
      }
      if (e.key === 'ArrowRight') { 
        e.preventDefault(); 
        navigate(ply + 1); 
      }
      if (e.key === 'Home') { 
        e.preventDefault(); 
        navigate(0); 
      }
      if (e.key === 'End') { 
        e.preventDefault(); 
        navigate(game.moves.length); 
      }
      }}>
      <section className="board-section" aria-label="Interactive game board">
        <div className="player-strip">
          <span className="avatar">{orientation === 'white' ? '♟' : '♙'}</span>
          <strong>{orientation === 'white' ? game.black : game.white}</strong>
          <small>{(orientation === 'white' ? game.blackRating : game.whiteRating) ?? 'Unrated'}</small>
        </div>
        <div className="board-wrap">
          <Chessboard options={{ id: `review-${game.id}`, 
            position: position?.fen ?? local.fen(), 
            boardOrientation: orientation, 
            allowDragging: !busy && !!position,
            darkSquareStyle: { backgroundColor: '#8d7961' }, 
            lightSquareStyle: { backgroundColor: '#ece2cd' }, 
            squareStyles: styles,
            animationDurationInMs: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 160,
            onSquareClick: ({ square }) => { if (selected && choose(selected, square)) return; setSelected(square); },
            onPieceDrop: ({ sourceSquare, targetSquare }) => targetSquare ? choose(sourceSquare, targetSquare) : false, }} />
        </div>

        <div className="player-strip">
          <span className="avatar light">{orientation === 'white' ? '♙' : '♟'}</span>
          <strong>{orientation === 'white' ? game.white : game.black}</strong>
          <small>{(orientation === 'white' ? game.whiteRating : game.blackRating) ?? 'Unrated'}</small>
        </div>
        <div className="board-controls">
          <button aria-label="First position" disabled={ply === 0 && !variation.length} onClick={() => 
            navigate(0)}>⏮</button>
          <button aria-label="Previous move" disabled={ply === 0 && !variation.length} onClick={() => 
            variation.length ? setVariation(v => v.slice(0, -1)) : navigate(ply - 1)}>←</button>
          <span>{variation.length ? `Variation · ${variation.length} half-moves` : `${ply} / ${game.moves.length}`}</span>
          <button aria-label="Next move" disabled={ply === game.moves.length} onClick={() => 
            navigate(ply + 1)}>→</button>
          <button aria-label="Last position" disabled={ply === game.moves.length && !variation.length} onClick={() => 
            navigate(game.moves.length)}>⏭</button>
          <button aria-label="Flip board" onClick={() => 
            setOrientation(o => o === 'white' ? 'black' : 'white')}>↻</button>
        </div>

        <p className="board-hint">Use ← → to replay. Select a piece, then a square, to explore.</p>

        {!variation.length && moveAnalysis && <EvaluationBar move={moveAnalysis} />}
        {analysis.data && analysis.data.moves.length > 0 && <EvaluationGraph run={analysis.data} ply={ply} onSelect={navigate} />}
      </section>
      <div className="review-side">
        <Panel>
          <div className="section-title">
            <h2>Moves</h2>
            <span className="pill">{variation.length ? 'EXPLORING' : 'MAINLINE'}</span>
          </div>
          <div className="notation" aria-label="Game notation">
            {game.moves.map(m => 
              <button key={m.ply} aria-label={`Move ${m.ply}: ${m.san}`} aria-current={m.ply === ply ? 'step' : undefined} onClick={() => 
                navigate(m.ply)}>
                  <small>{Math.floor(Number(m.fenBefore.split(' ')[5]))}{m.side === 'white' ? '.' : '…'}</small>{m.san}</button>
                )
            }
          </div>
          <p className="metadata">{game.termination ?? 'Termination not recorded'}</p>
        </Panel>
        <Panel>
          <p className="eyebrow">THE SELECTED POSITION</p>
          <h2>{position?.endgame ?? `${position?.sideToMove ?? (local.turn() === 'w' ? 'White' : 'Black')} to move`}</h2>
          <p role="status" aria-label="Position status">{busy ? 'Checking position…' : variation.length ? 'Exploring an alternative line. Your original game is preserved.' : move ? `After ${move.san}. ${move.clockSeconds === null ? 'Clock unavailable for this move.' : `${move.clockSeconds.toFixed(1)} seconds remained on the moving player’s clock.`}` : 'The starting position of your game.'}</p>
          <ErrorNotice message={error} />
          
          {
            variation.length > 0 && 
              <>
                <p className="variation-line">{local.history().join(' ')}</p>
                <button onClick={() => { setVariation([]); setPromotion(undefined); }}>Return to mainline</button>
              </>
          }

          {
            promotion && 
            <fieldset className="promotion">
              <legend>Promote your pawn to</legend>
              {
                [['q', 'Queen'], ['r', 'Rook'], ['b', 'Bishop'], ['n', 'Knight']].map(([piece, label]) => 
                  <button key={piece} onClick={() => play(promotion + piece)}>{label}</button>)
              }
              <button onClick={() => setPromotion(undefined)}>Cancel</button>
            </fieldset>
          }
          <form className="coordinate-form" onSubmit={keyboardMove}>
            <label>Explore a move
              <input aria-label="Coordinate move" value={typed} onChange={e => 
                setTyped(e.target.value)} 
                placeholder="e2e4 · a7a8q" 
                maxLength={5} autoComplete="off" /></label>
            <button disabled={busy || !typed}>Play move</button>
          </form>
          <details>
            <summary>Legal moves and position text</summary>
            <p className="mono">{position?.legalMoves.join(' · ') || (busy ? 'Loading…' : 'No legal moves.')}</p>
            <p className="mono">{position?.fen ?? local.fen()}</p>
          </details>
        </Panel>
        <PositionAnalysisPanel gameId={game.id} 
          ply={ply} 
          variation={variation} fen={position?.fen ?? local.fen()} 
          ready={!busy && !!position?.legalMoves.length} 
          revision={analysis.positionRevision} />
        <AnalysisPanel game={game} run={analysis.data} current={moveAnalysis} 
          exploring={variation.length > 0} error={analysis.error} 
          reload={analysis.reload} connected={analysis.connected} 
          readOnly={!!runId} />
      </div>
    </div>
    <details className="source-pgn">
      <summary>Original PGN</summary>
      <pre>{game.rawPgn}</pre>
    </details>
  </>;
}
