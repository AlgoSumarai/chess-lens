import { useEffect, useMemo, useRef, useState, type CSSProperties, type FormEvent } from 'react';
import { Chess } from 'chess.js';
import { Chessboard } from 'react-chessboard';
import { api, send } from './api';
import { useRemote } from './hooks';
import { useSavedJob } from './jobHooks';
import { ErrorNotice, Panel } from './App';

type Score = { kind: string; value: number };
type Search = { position: { initialFen: string; history: string[] }; result: { engine: string; settingsJson: string; bestMove: string; lines: { score: Score; pv: string[] }[] }; budget: { profile: string; nodes: number } };
type Pair = { first: Search; final: Search };
type Grade = { decision: number; move: string; accepted: boolean; reason: string; best: Pair; played: Pair };
type Objective = { kind: string; title: string; instruction: string; decisions: number };
type AttemptSummary = { id: string; number: number; state: string; hintsUsed: number; undosUsed: number };
type Attempt = AttemptSummary & { objective: Objective; assisted: boolean; initialFen: string; fen: string; endgame: string | null; history: string[]; notation: string[];
  legalMoves: string[]; hint: string | null; decisionsPlayed: number; review: null | { gameId: string; runId: string; ply: number; weaknessSnapshotId: string | null; rulesVersion: string; grades: Grade[]; initialCheck: Pair | null; finalCheck: Pair | null; conclusion: string | null } };
type Session = { id: string; state: string; version: number; side: 'white' | 'black'; objective: Objective; error: string | null; opponent: string; attempts: AttemptSummary[]; attempt: Attempt };
const outcome = (state: string) => ({ succeeded: 'Objective met', 'needs-practice': 'A decision to practise again', ungraded: 'Ungraded', retried: 'Restarted', cancelled: 'Cancelled', active: 'In progress' }[state] ?? state);

export function CreatePracticeButton({ runId, ply, weaknessSnapshotId }: { runId: string; ply: number; weaknessSnapshotId?: string }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  return <div className="practice-create"><button className="primary" disabled={busy} onClick={() => {
    setBusy(true); setError(''); void send<Session>('/practice', { runId, ply, weaknessSnapshotId: weaknessSnapshotId ?? null })
      .then(s => { location.hash = `/practice/${s.id}`; }).catch(e => setError(e.message)).finally(() => setBusy(false));
  }}>{busy ? 'Opening saved practice…' : 'Play from before this mistake'}</button><small>You keep your colour. Practise three decisions against a practical opponent.</small><ErrorNotice message={error} /></div>;
}

export function PracticeLibrary() {
  const [page, setPage] = useState(1);
  const { data, error, reload } = useRemote<{ total: number; items: (Pick<Session, 'id' | 'state' | 'side' | 'objective' | 'error'> & { updatedAt: string })[] }>(`/practice?page=${page}`, 5000);
  return <><div className="page-heading compact"><div><p className="eyebrow">REHEARSE YOUR NEXT DECISION</p><h1>Mistake practice</h1><p>Return to the moment before a mistake. Play your colour, then review what you learned.</p></div></div>
    <ErrorNotice message={error} retry={reload} />{!data && !error && <p aria-busy="true">Loading saved sessions…</p>}
    {data?.total === 0 && <Panel className="empty"><h2>Start from a checked decision.</h2><p>Open a supported weakness or a verified mistake in your game review and choose “Play from before this mistake”. Each attempt covers three decisions, with a clear learning objective.</p><a className="button primary" href="#/weaknesses">Find a decision to practise</a><p><a href="#/games">Review your games</a></p></Panel>}
    <div className="puzzle-list">{data?.items.map(s => <Panel key={s.id}><div className="section-title"><h2>{s.objective.title}</h2><span className="pill">{s.state === 'completed' ? 'Ready to review' : s.state}</span></div><p>You play {s.side}. {s.objective.instruction}</p><p>{s.error}</p><small>Saved {new Date(s.updatedAt).toLocaleString()}</small><p><a className="button primary" href={`#/practice/${s.id}`}>{s.state === 'completed' ? 'Review or try again' : 'Open saved session'}</a></p></Panel>)}</div>
    {!!data && data.total > 20 && <div className="pagination"><button disabled={page === 1} onClick={() => setPage(p => p - 1)}>Previous</button><span>Page {page} of {Math.ceil(data.total / 20)}</span><button disabled={page * 20 >= data.total} onClick={() => setPage(p => p + 1)}>Next</button></div>}</>;
}

export function PracticePage({ id, attemptId }: { id: string; attemptId?: string }) {
  const { data, error, reload, connected } = useSavedJob<Session>(`/practice/${id}`, id, 'practice');
  const [local, setLocal] = useState<Session>(); const [historical, setHistorical] = useState<Attempt>();
  const [busy, setBusy] = useState(false); const [problem, setProblem] = useState(''); const sending = useRef(false);
  const session = local && (!data || local.version >= data.version) ? local : data;
  useEffect(() => {
    if (!attemptId) return;
    const controller = new AbortController();
    void api<Attempt>(`/practice/${id}/attempts/${attemptId}`, { signal: controller.signal }).then(setHistorical)
      .catch(e => { if (!controller.signal.aborted) setProblem(e.message); });
    return () => controller.abort();
  }, [id, attemptId]);
  const command = async (action: string, move?: string) => {
    if (!session || sending.current || attemptId) return;
    sending.current = true; setBusy(true); setProblem('');
    try { setLocal(await send<Session>(`/practice/${id}/${action}`, { version: session.version, attemptId: session.attempt.id, move: move ?? null })); reload(); }
    catch (e) { setProblem((e as Error).message); reload(); } finally { sending.current = false; setBusy(false); }
  };
  if (!session || attemptId && !historical) return <><a href="#/practice">← Mistake practice</a><ErrorNotice message={error || problem} retry={reload} /><p aria-busy="true">Opening saved practice…</p></>;
  const attempt = historical ?? session.attempt;
  const objective = attempt.objective;
  const active = !attemptId && session.state === 'active' && attempt.state === 'active';
  const waiting = !attemptId && ['waiting', 'preparing'].includes(session.state);
  return <><a className="back-link" href="#/practice">← Mistake practice</a><div className="page-heading compact"><div><p className="eyebrow">YOUR COLOUR. A NEW CONTINUATION.</p><h1>{session.state === 'preparing' && !attemptId ? 'Checking your practice position' : objective.title}</h1><p>You play {session.side}. {objective.instruction}</p></div></div>
    <div className="review-grid training-grid"><PracticeBoard attempt={attempt} side={session.side} active={active && !busy} play={move => void command('moves', move)} />
      <div className="review-side"><Panel><h2>{attempt.state === 'active' ? `Attempt ${attempt.number}` : outcome(attempt.state)}</h2>
        <p role="status" aria-label="Mistake practice status">{busy ? 'Saving your command…' : waiting ? session.state === 'preparing' ? 'Confirming the starting objective with full-strength checks…' : 'Checking your decision and preparing the opponent’s reply…' : active ? 'Your move. Select a piece and destination, or enter a coordinate move.' : `${outcome(attempt.state)} · ${attempt.assisted ? 'assisted' : 'unassisted'} attempt.`}</p>
        <ErrorNotice message={error || problem || (!attemptId ? session.error ?? '' : '')} retry={reload} />
        <p>{Math.min(attempt.decisionsPlayed, objective.decisions)} of {objective.decisions} player decisions · {attempt.hintsUsed} hints · {attempt.undosUsed} undos</p>
        {attempt.endgame && <p>Position ended: {attempt.endgame}.</p>}
        {!attemptId && attempt.state === 'active' && <div className="analysis-actions"><button disabled={busy || !active || attempt.hintsUsed >= 6} onClick={() => void command('hint')}>Get a hint</button><button disabled={busy || attempt.history.length === 0 || session.state === 'preparing' || attempt.undosUsed >= 6} onClick={() => void command('undo')}>Undo last decision</button><button disabled={busy} onClick={() => void command('cancel')}>End attempt</button></div>}
        {attempt.hint && <p className="practice-note" role="status">{attempt.hint}</p>}
        <small>Hints and undo mark this attempt as assisted. Any legal continuation meeting the checked objective is accepted.</small>
        {!attemptId && <p><button disabled={busy} onClick={() => void command('retry')}>{attempt.state === 'active' ? 'Restart with a fresh attempt' : 'Try again'}</button></p>}
        {attemptId && <p><a className="button primary" href={`#/practice/${id}`}>Return to current attempt</a></p>}
        {waiting && <p>{connected ? 'Progress updates connected.' : 'Reconnecting; your session is saved.'} You can leave and resume here.</p>}
        <p><small>{session.opponent}. Grading uses separate full-strength checks and a bounded continuation, not a whole-game win.</small></p>
      </Panel>
      {attempt.review && <PracticeReview attempt={attempt} />}
      {session.attempts.length > 1 && <Panel><h2>Your saved attempts</h2><ul className="attempt-list">{session.attempts.map(a => <li key={a.id}>{a.state === 'active' ? <span>Attempt {a.number} · in progress</span> : <a href={`#/practice/${id}?attempt=${a.id}`}>Attempt {a.number} · {outcome(a.state)} · {a.hintsUsed + a.undosUsed ? 'assisted' : 'unassisted'}</a>}</li>)}</ul></Panel>}
      </div></div></>;
}

function PracticeBoard({ attempt, side, active, play }: { attempt: Attempt; side: 'white' | 'black'; active: boolean; play: (move: string) => void }) {
  const [orientation, setOrientation] = useState(side); const [selected, setSelected] = useState<string>();
  const [promotion, setPromotion] = useState<string>(); const [typed, setTyped] = useState(''); const [error, setError] = useState(''); const [playback, setPlayback] = useState<number | null>(null);
  useEffect(() => { setSelected(undefined); setPromotion(undefined); setTyped(''); setError(''); setPlayback(null); }, [attempt.id, attempt.fen, active]);
  const replay = useMemo(() => { const board = new Chess(attempt.initialFen); for (const move of attempt.history.slice(0, playback ?? attempt.history.length)) board.move({ from: move.slice(0, 2), to: move.slice(2, 4), promotion: move[4] }); return board; }, [attempt.initialFen, attempt.history, playback]);
  const choose = (from: string, to: string) => {
    if (!active) return false; const moves = attempt.legalMoves.filter(m => m.startsWith(from + to));
    if (moves.length > 1) { setPromotion(from + to); return true; }
    if (moves.length === 1) { play(moves[0]); return true; } return false;
  };
  const submit = (event: FormEvent) => { event.preventDefault(); const move = typed.trim().toLowerCase();
    if (attempt.legalMoves.includes(move)) play(move); else if (move.length !== 4 || !choose(move.slice(0, 2), move.slice(2, 4))) setError('Enter a legal coordinate move, such as e2e4 or a7a8q.'); };
  const styles: Record<string, CSSProperties> = {};
  if (selected && active) { styles[selected] = { backgroundColor: '#bc934e' }; for (const move of attempt.legalMoves.filter(m => m.startsWith(selected))) styles[move.slice(2, 4)] = { backgroundImage: 'radial-gradient(circle, #2b3c4280 22%, transparent 24%)' }; }
  return <section className="board-section" aria-label="Mistake practice board"><div className="board-wrap"><Chessboard options={{ id: `practice-${attempt.id}`, position: playback === null ? attempt.fen : replay.fen(), boardOrientation: orientation, allowDragging: active, squareStyles: styles,
    darkSquareStyle: { backgroundColor: '#8d7961' }, lightSquareStyle: { backgroundColor: '#ece2cd' }, animationDurationInMs: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 160,
    onSquareClick: ({ square }) => { if (!active) return; if (selected && choose(selected, square)) return; setSelected(square); },
    onPieceDrop: ({ sourceSquare, targetSquare }) => targetSquare ? choose(sourceSquare, targetSquare) : false,
  }} /></div><div className="board-controls"><button onClick={() => setOrientation(o => o === 'white' ? 'black' : 'white')}>Flip board</button><span>{playback === null ? 'Saved practice position' : `Review ${playback}/${attempt.history.length}`}</span></div>
    <p className="variation-line" aria-label="Practice notation">{(playback === null ? attempt.notation : replay.history()).join(' ') || 'Position immediately before your mistake'}</p>
    {active && <form className="practice-move-form" onSubmit={submit}><label>Coordinate move<input value={typed} onChange={e => setTyped(e.target.value)} maxLength={5} placeholder="e2e4" autoComplete="off" /></label><button className="primary">Play move</button></form>}
    {promotion && active && <fieldset className="promotion"><legend>Promote to</legend>{[['q', 'Queen'], ['r', 'Rook'], ['b', 'Bishop'], ['n', 'Knight']].map(([piece, label]) => <button key={piece} onClick={() => play(promotion + piece)}>{label}</button>)}<button onClick={() => setPromotion(undefined)}>Cancel promotion</button></fieldset>}
    <ErrorNotice message={error} />
    {attempt.review && <div className="board-controls"><button disabled={playback === 0} onClick={() => setPlayback(n => Math.max(0, (n ?? attempt.history.length) - 1))}>Previous practice move</button><button disabled={playback === null || playback === attempt.history.length} onClick={() => setPlayback(n => Math.min(attempt.history.length, (n ?? 0) + 1))}>Next practice move</button><button onClick={() => setPlayback(null)}>Final position</button></div>}
  </section>;
}

function PracticeReview({ attempt }: { attempt: Attempt }) {
  const review = attempt.review!;
  const line = (search: Search) => { const board = new Chess(search.position.initialFen); for (const move of search.position.history) board.move({ from: move.slice(0, 2), to: move.slice(2, 4), promotion: move[4] });
    return search.result.lines[0].pv.slice(0, 6).map(move => board.move({ from: move.slice(0, 2), to: move.slice(2, 4), promotion: move[4] }).san).join(' '); };
  const score = (value: Score) => value.kind === 'mate' ? `${value.value > 0 ? 'Forced mate' : 'Facing forced mate'} in ${Math.abs(value.value)}` : `${value.value > 0 ? '+' : ''}${(value.value / 100).toFixed(2)}`;
  return <Panel><p className="eyebrow">YOUR CHECKED CONTINUATION</p><h2>Review the attempt</h2><p>{attempt.assisted ? 'This attempt used help. Keep it separate from your unassisted results.' : 'No hints or undo were used in this attempt.'} The result describes this short learning objective.</p>
    {review.conclusion && <p>{review.conclusion}</p>}{review.finalCheck && <p>Final checked position: {score(review.finalCheck.final.result.lines[0].score)} from your perspective.</p>}
    {review.grades.length === 0 && <p>No complete decision grade was saved. An ungraded or cancelled attempt is not a failed chess answer.</p>}
    {review.grades.map(g => <div className="practice-grade" key={g.decision}><h3>Decision {g.decision} · {g.move} · {g.accepted ? 'met the objective' : 'practise again'}</h3><p>{g.reason}</p>
      <p>Best: {score(g.best.final.result.lines[0].score)} · Your move: {score(g.played.final.result.lines[0].score)} · from your perspective</p><p><strong>A checked alternative:</strong> {line(g.best.final)}</p><small>{g.best.final.result.engine} · {g.best.first.budget.nodes.toLocaleString()} / {g.best.final.budget.nodes.toLocaleString()} node ceilings · exact, legal evidence at both budgets</small></div>)}
    <p><a href={`#/games/${review.gameId}?ply=${review.ply}&run=${review.runId}`}>Review the exact original mistake</a></p>{review.weaknessSnapshotId && <p><a href={`#/weaknesses?snapshot=${review.weaknessSnapshotId}`}>Return to the saved supporting weakness</a></p>}
    <small>{review.rulesVersion}. Centipawn scores are bounded engine estimates; mate scores remain separate.</small>
  </Panel>;
}
