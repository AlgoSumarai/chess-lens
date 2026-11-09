import { StatusMessage } from './StatusMessage';
import { useEffect, useMemo, useReducer, useState, type CSSProperties, type FormEvent } from 'react';
import { Chess } from 'chess.js';
import { Chessboard } from 'react-chessboard';
import { namedPieces } from './chessPieces';
import { api, send, type Game, type Position } from './api';
import { useRemote } from './hooks';
import { ErrorNotice, Panel } from './App';
import { useAnalysis } from './analysis';
import { AnalysisPanel, EvaluationGraph } from './AnalysisPanel';
import { PositionAnalysisPanel } from './PositionAnalysisPanel';
import { usePositionAnalysis } from './positionAnalysis';
import { LiveEvaluationBar } from './LiveEvaluationBar';
import { createReviewTree, moveNumber, reviewPath, reviewReducer, reviewVariations } from './reviewTree';
import { BoardPositionText } from './BoardPositionText';
import { PromotionChoices } from './PromotionChoices';

export function GameReview({ id, initialPly = 0, runId, coachingLevel = 'beginner', modelOptIn = false }: { id: string; initialPly?: number; runId?: string; coachingLevel?: string; modelOptIn?: boolean }) {
  const { data, error, reload } = useRemote<Game>(`/games/${id}`);
  const [lastGame, setLastGame] = useState<Game>();
  useEffect(() => { if (data) setLastGame(data); }, [data]);
  // Refreshing player identity must not close this review and discard its lines.
  const game = data?.id === id ? data : lastGame?.id === id ? lastGame : undefined;
  if (error && !game) return <ErrorNotice message={error} retry={reload} />;
  if (!game) return <p aria-busy="true">Opening your game…</p>;
  return <>
    <ErrorNotice message={error} retry={reload} />
    {runId && <Panel>
        <p>Viewing the saved analysis referenced by this evidence.</p>
        <a href={`#/games/${id}?ply=${initialPly}`}>Open the latest review</a>
      </Panel>}
    <Review key={game.id} game={game} reload={reload} initialPly={initialPly} runId={runId} coachingLevel={coachingLevel} modelOptIn={modelOptIn} />
    </>;
}

function Review({ game, reload, initialPly, runId, coachingLevel, modelOptIn }: { game: Game; reload: () => void; initialPly: number; runId?: string; coachingLevel: string; modelOptIn: boolean }) {
  const analysis = useAnalysis(game.id, runId);
  const [tree, dispatch] = useReducer(reviewReducer, { game, initialPly }, createReviewTree);
  const node = tree.nodes[tree.current];
  const { ply, variation } = node;
  const selectedPath = useMemo(() => reviewPath(tree, tree.line), [tree]);
  const cursor = selectedPath.findIndex(n => n.id === tree.current);
  const variationLines = reviewVariations(tree);
  const mainlineEnd = tree.mainline[tree.mainline.length - 1];
  const [orientation, setOrientation] = useState<'white' | 'black'>(game.playerColour ?? 'white');
  const [selected, setSelected] = useState<string>();
  const [positionResponse, setPosition] = useState<Position & { requestKey: string }>();
  const requestKey = `${game.id}/${ply}/${variation.join(' ')}`;
  const position = positionResponse?.requestKey === requestKey ? positionResponse : undefined;
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState('');
  const [promotion, setPromotion] = useState<string>();
  const [typed, setTyped] = useState('');
  const local = useMemo(() => new Chess(node.fen), [node.fen]);
  const fen = position?.fen ?? local.fen();
  const liveAnalysis = usePositionAnalysis({ gameId: game.id, ply, variation, fen,
    ready: !busy && !!position, legalMoves: position?.legalMoves ?? [], revision: analysis.positionRevision });

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

  const clearInput = () => { setSelected(undefined); setPromotion(undefined); setTyped(''); setError(''); };
  const navigate = (next: number) => {
    dispatch({ type: 'select', node: tree.mainline[Math.max(0, Math.min(game.moves.length, next))], line: mainlineEnd });
    clearInput();
  };
  const step = (direction: 'first' | 'previous' | 'next' | 'last') => { dispatch({ type: 'navigate', direction }); clearInput(); };
  
  const play = (uci: string) => {
    if (busy || !position?.legalMoves.includes(uci)) { 
      setError('Choose a legal move in the current position.'); 
      return false; 
    }
    if (variation.length >= 100 && !node.children.some(id => tree.nodes[id].uci === uci)) {
      setError('This variation has reached 100 half-moves. Go back to explore another continuation.');
      return false;
    }
    dispatch({ type: 'play', uci });
    clearInput();
    
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
  const last = node.uci;
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
      if (uci.length === 4 && position?.legalMoves.some(m => m.startsWith(uci) && m.length === 5)) {
        setError(''); choose(uci.slice(0, 2), uci.slice(2, 4));
      } else play(uci);
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
      if ((e.target as HTMLElement).closest('[aria-roledescription="draggable"]')) return;
      if (e.key === 'ArrowLeft') { 
        e.preventDefault(); 
        step('previous');
      }
      if (e.key === 'ArrowRight') { 
        e.preventDefault(); 
        step('next');
      }
      if (e.key === 'Home') { 
        e.preventDefault(); 
        step('first');
      }
      if (e.key === 'End') { 
        e.preventDefault(); 
        step('last');
      }
      }}>
      <section className="board-section" aria-label="Interactive game board">
        <div className="player-strip">
          <span className="avatar">{orientation === 'white' ? '♟' : '♙'}</span>
          <strong>{orientation === 'white' ? game.black : game.white}</strong>
          <small>{(orientation === 'white' ? game.blackRating : game.whiteRating) ?? 'Unrated'}</small>
        </div>
        <div className="board-with-evaluation">
          <LiveEvaluationBar analysis={liveAnalysis} orientation={orientation} />
          <div className="board-wrap">
            <Chessboard options={{ pieces: namedPieces, lightSquareNotationStyle: { color: '#101010' }, darkSquareNotationStyle: { color: '#101010' }, id: `review-${game.id}`,
              position: fen,
              boardOrientation: orientation,
              allowDragging: !busy && !!position,
              darkSquareStyle: { backgroundColor: '#8d7961' },
              lightSquareStyle: { backgroundColor: '#ece2cd' },
              squareStyles: styles,
              animationDurationInMs: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 160,
              onSquareClick: ({ square }) => { if (selected && choose(selected, square)) return; setSelected(square); },
              onPieceDrop: ({ sourceSquare, targetSquare }) => targetSquare ? choose(sourceSquare, targetSquare) : false, }} />
          </div>
        </div>

        <div className="player-strip">
          <span className="avatar light">{orientation === 'white' ? '♙' : '♟'}</span>
          <strong>{orientation === 'white' ? game.white : game.black}</strong>
          <small>{(orientation === 'white' ? game.whiteRating : game.blackRating) ?? 'Unrated'}</small>
        </div>
        <div className="board-controls">
          <button aria-label="First position" aria-disabled={cursor === 0} onClick={() => step('first')}>⏮</button>
          <button aria-label="Previous move" aria-disabled={cursor === 0} onClick={() => step('previous')}>←</button>
          <span>{tree.line === mainlineEnd ? `${ply} / ${game.moves.length}` : `Variation · ${cursor} / ${selectedPath.length - 1}`}</span>
          <button aria-label="Next move" aria-disabled={cursor === selectedPath.length - 1} onClick={() => step('next')}>→</button>
          <button aria-label="Last position" aria-disabled={cursor === selectedPath.length - 1} onClick={() => step('last')}>⏭</button>
          <button aria-label="Flip board" onClick={() => 
            setOrientation(o => o === 'white' ? 'black' : 'white')}>↻</button>
        </div>

        <p className="board-hint">Use ← → to replay. Select a piece, then a square, to explore.</p>

        {analysis.data && analysis.data.moves.length > 0 && <EvaluationGraph run={analysis.data} ply={variation.length ? -1 : ply} onSelect={navigate} showScores={liveAnalysis.enabled} />}
      </section>
      <div className="review-side">
        <Panel>
          <div className="section-title">
            <h2>Moves</h2>
            <span className="pill">{tree.line === mainlineEnd ? 'MAINLINE' : 'EXPLORING'}</span>
          </div>
          <div className="review-lines">
            <button className="line-selector" aria-label="Select original game" aria-pressed={tree.line === mainlineEnd} onClick={() => { dispatch({ type: 'line', line: mainlineEnd }); clearInput(); }}>Original game</button>
            <div className="notation" role="group" aria-label="Game notation">
              {game.moves.map(m => <button key={m.ply} aria-label={`Move ${m.ply}: ${m.san}`} aria-current={tree.current === tree.mainline[m.ply] ? 'step' : undefined} onClick={() => navigate(m.ply)}>
                <small>{moveNumber(m.fenBefore)}</small>{m.san}</button>)}
            </div>
            {variationLines.map((end, index) => {
              const path = reviewPath(tree, end.id);
              const branch = path.filter(n => n.variation.length);
              const anchor = tree.nodes[branch[0].parent!];
              return <div className="review-variation" key={end.id}>
                <button className="line-selector" aria-label={`Select variation ${index + 1}`} aria-pressed={tree.line === end.id} onClick={() => { dispatch({ type: 'line', line: end.id }); clearInput(); }}>Variation {index + 1} · {anchor.san ? `after ${moveNumber(tree.nodes[anchor.parent!].fen)} ${anchor.san}` : 'from start'}</button>
                <div className="variation-notation" role="group" aria-label={`Variation ${index + 1} moves`}>
                  {branch.map(n => <button key={n.id} aria-label={`Variation ${index + 1}: ${moveNumber(tree.nodes[n.parent!].fen)} ${n.san}`} aria-current={tree.current === n.id ? 'step' : undefined} onClick={() => { dispatch({ type: 'select', node: n.id, line: end.id }); clearInput(); }}>
                    <small>{moveNumber(tree.nodes[n.parent!].fen)}</small> {n.san}</button>)}
                </div>
              </div>;
            })}
          </div>
          <p className="metadata">{game.termination ?? 'Termination not recorded'}</p>
        </Panel>
        <Panel>
          <p className="eyebrow">THE SELECTED POSITION</p>
          <h2>{position?.endgame ?? `${position?.sideToMove ?? (local.turn() === 'w' ? 'White' : 'Black')} to move`}</h2>
          <StatusMessage aria-label="Position status">{busy ? 'Checking position…' : variation.length ? 'Exploring an alternative line. Your original game is preserved.' : move ? `After ${move.san}. ${move.clockSeconds === null ? 'Clock unavailable for this move.' : `${move.clockSeconds.toFixed(1)} seconds remained on the moving player’s clock.`}` : 'The starting position of your game.'}</StatusMessage>
          <ErrorNotice message={error} />
          
          {
            variation.length > 0 && 
              <>
                <p className="variation-line">{reviewPath(tree, tree.current).filter(n => n.variation.length).map(n => n.san).join(' ')}</p>
                <button onClick={() => navigate(ply)}>Return to mainline</button>
              </>
          }

          {
            promotion && 
            <PromotionChoices choose={piece => { play(promotion + piece); }} cancel={() => setPromotion(undefined)} />
          }
          <form className="coordinate-form" onSubmit={keyboardMove}>
            <label>Explore a move
              <input aria-label="Coordinate move" value={typed} onChange={e => 
                setTyped(e.target.value)} 
                placeholder="e2e4 · a7a8q" 
                maxLength={5} autoComplete="off" /></label>
            <button disabled={busy || !typed}>Play move</button>
          </form>
          <BoardPositionText fen={fen} legalMoves={position?.legalMoves} />
        </Panel>
        <PositionAnalysisPanel analysis={liveAnalysis} fen={fen} onPlay={play} />
        <AnalysisPanel game={game} run={analysis.data} current={moveAnalysis} coachingLevel={coachingLevel} modelOptIn={modelOptIn}
          exploring={variation.length > 0} error={analysis.error} 
          reload={analysis.reload} connected={analysis.connected} 
          readOnly={!!runId} />
      </div>
    </div>
    <details className="source-pgn">
      <summary>{game.annotationState === 'unattributed' ? 'Saved game PGN' : 'Original PGN'}</summary>
      {game.annotationState === 'unattributed' && <p>Older annotations could not be attributed to your workspace. Reimport your original PGN to restore your comments and clocks; this saved game and its reviews stay available.</p>}
      <pre>{game.rawPgn}</pre>
    </details>
  </>;
}
