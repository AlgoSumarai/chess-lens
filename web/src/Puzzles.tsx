import { useMemo, useRef, useState, type CSSProperties, type FormEvent } from 'react';
import { Chess } from 'chess.js';
import { Chessboard } from 'react-chessboard';
import { send } from './api';
import { useRemote } from './hooks';
import { useSavedJob } from './jobHooks';
import { ErrorNotice, Panel } from './App';

type Puzzle = { id: string; state: string; version: number; error: string | null; objective: string | null; materialTarget: number | null; side: 'white' | 'black' | null; reviewDueAt: string | null; difficulty: string };
type Choice = { move: string; reply: string | null; next: SolutionNode | null; resolved: boolean };
type SolutionNode = { fen: string; choices: Choice[] };
type Attempt = { id: string; puzzleId: string; state: string; version: number; hintsUsed: number; solveSeconds: number | null; reviewDueAt: string | null;
  side: 'white' | 'black'; objective: string; materialTarget: number; initialFen: string; fen: string; legalMoves: string[]; history: string[]; hint: string | null;
  review: null | { gameId: string; runId: string; ply: number; explanation: string; root: SolutionNode; version: string; engine: string; journal: { move: string; accepted: boolean }[] } };
const objective = (kind: string | null, target: number | null) => kind === 'mate' ? 'Find the forced mate' : kind === 'material' ? `Win ${(target ?? 0) / 100} points of material` : 'Checking the learning objective';
const due = (value: string | null) => !value ? '' : new Date(value).getTime() <= Date.now() ? 'Ready to review' : `Review ${new Date(value).toLocaleString()}`;

export function CreatePuzzleButton({ runId, ply }: { runId: string; ply: number }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  return <div className="puzzle-create"><button disabled={busy} onClick={() => { setBusy(true); setError('');
    void send<Puzzle>('/puzzles', { runId, ply }).then(p => { location.hash = `/puzzles/${p.id}`; }).catch(e => setError(e.message)).finally(() => setBusy(false));
  }}>{busy ? 'Queuing validation…' : 'Turn this decision into a puzzle'}</button><small>Only stable, focused tactics qualify after deeper checks.</small><ErrorNotice message={error} /></div>;
}

export function PuzzleLibrary() {
  const [page, setPage] = useState(1);
  const { data, error, reload } = useRemote<{ items: Puzzle[]; total: number }>(`/puzzles?page=${page}`, 2500);
  return <><div className="page-heading compact"><div><p className="eyebrow">A BETTER NEXT DECISION</p><h1>Your puzzle practice</h1><p>Short, engine-checked lessons from your own completed games.</p></div></div><ErrorNotice message={error} retry={reload} />
    {!data && !error && <p aria-busy="true">Loading your practice…</p>}
    {data?.total === 0 && <Panel className="empty"><h2>Find a decision worth practising.</h2><p>Open a verified mistake in a completed game review and ask for a puzzle. Positions without a stable tactical objective are declined.</p><a className="button primary" href="#/games">Review your games</a><p><a href="#/weaknesses">Inspect your patterns</a></p></Panel>}
    <div className="puzzle-list">{data?.items.map(p => <Panel key={p.id}><div className="section-title"><h2>{objective(p.objective, p.materialTarget)}</h2><span className="pill">{p.state}</span></div><p>{p.side && `You play ${p.side}. `}{p.difficulty}.</p><p>{p.error ?? (p.state === 'ready' ? due(p.reviewDueAt) : 'Validation checks both solutions and defensive replies.')}</p><a className="button" href={`#/puzzles/${p.id}`}>{p.state === 'ready' ? 'Open puzzle' : 'View validation'}</a></Panel>)}</div>
    {!!data && data.total > 20 && <div className="pagination"><button disabled={page === 1} onClick={() => setPage(p => p - 1)}>Previous</button><span>Page {page} of {Math.ceil(data.total / 20)}</span><button disabled={page * 20 >= data.total} onClick={() => setPage(p => p + 1)}>Next</button></div>}</>;
}

export function PuzzlePage({ id }: { id: string }) {
  const { data: puzzle, error, reload, connected } = useSavedJob<Puzzle>(`/puzzles/${id}`, id, 'puzzle'); const [busy, setBusy] = useState(false); const [problem, setProblem] = useState('');
  const start = () => { setBusy(true); void send<Attempt>(`/puzzles/${id}/attempts`).then(a => { location.hash = `/puzzles/attempts/${a.id}`; }).catch(e => setProblem(e.message)).finally(() => setBusy(false)); };
  return <><a className="back-link" href="#/puzzles">← Puzzle practice</a><div className="page-heading compact"><div><p className="eyebrow">FROM A PAST DECISION</p><h1>{objective(puzzle?.objective ?? null, puzzle?.materialTarget ?? null)}</h1></div></div><ErrorNotice message={error || problem} retry={reload} />
    {puzzle ? <Panel><p role="status" aria-label="Puzzle validation status">{puzzle.state === 'ready' ? 'Validated and ready to solve.' : `Puzzle validation: ${puzzle.state}.`}</p>{puzzle.error && <p>{puzzle.error}</p>}
      {puzzle.state === 'ready' ? <><p>You play {puzzle.side}. The original mistake and solution will appear after your attempt.</p><p>{puzzle.difficulty}. {due(puzzle.reviewDueAt)}.</p><button className="primary" disabled={busy} onClick={start}>{busy ? 'Opening…' : 'Start or resume puzzle'}</button></>
        : ['queued', 'running'].includes(puzzle.state) ? <><p>Checking deeper candidate lines, acceptable alternatives and the tactical payoff. This can take up to three minutes per attempt.</p><p>{connected ? 'Live updates connected.' : 'Reconnecting; validation progress is saved.'}</p><button onClick={() => void send(`/puzzles/${id}/cancel`).then(reload).catch(e => setProblem(e.message))}>Cancel validation</button></>
          : <><p>Not every mistake makes a useful puzzle. Choose another verified position from your games.</p><a href="#/games">Find another decision</a></>}
    </Panel> : !error && <p aria-busy="true">Loading validation…</p>}</>;
}

export function PuzzleAttemptPage({ id }: { id: string }) {
  const { data, error, reload } = useRemote<Attempt>(`/puzzles/attempts/${id}`);
  if (error) return <ErrorNotice message={error} retry={reload} />;
  return data ? <Solver initial={data} reload={reload} key={data.id} /> : <p aria-busy="true">Opening your saved attempt…</p>;
}

function Solver({ initial, reload }: { initial: Attempt; reload: () => void }) {
  const [attempt, setAttempt] = useState(initial); const [busy, setBusy] = useState(false); const sending = useRef(false);
  const [error, setError] = useState(''); const [selected, setSelected] = useState<string>(); const [promotion, setPromotion] = useState<string>(); const [typed, setTyped] = useState('');
  const [orientation, setOrientation] = useState(attempt.side); const [branches, setBranches] = useState<number[]>([]); const [playback, setPlayback] = useState<number | null>(null);
  const active = attempt.state === 'active';
  const solution = useMemo(() => {
    const moves: string[] = []; const decisions: SolutionNode[] = []; let node = attempt.review?.root;
    for (let index = 0; node && index < 3; index++) { decisions.push(node); const choice: Choice = node.choices[branches[index] ?? 0]; moves.push(choice.move); if (choice.reply) moves.push(choice.reply); node = choice.next ?? undefined; }
    return { moves, decisions };
  }, [attempt.review, branches]);
  const replay = useMemo(() => { const board = new Chess(attempt.initialFen); for (const move of solution.moves.slice(0, playback ?? 0)) board.move({ from: move.slice(0, 2), to: move.slice(2, 4), promotion: move[4] }); return board; }, [attempt.initialFen, solution.moves, playback]);
  const command = async (action: string, move?: string) => {
    if (sending.current) return; sending.current = true; setBusy(true); setError('');
    try { const next = await send<Attempt>(`/puzzles/attempts/${attempt.id}/${action}`, { version: attempt.version, move: move ?? null }); setAttempt(next); setSelected(undefined); setPromotion(undefined); setTyped(''); }
    catch (e) { setError((e as Error).message); } finally { sending.current = false; setBusy(false); }
  };
  const choose = (from: string, to: string) => { if (!active || busy) return false; const moves = attempt.legalMoves.filter(m => m.startsWith(from + to)); if (moves.length > 1) { setPromotion(from + to); return true; } if (moves.length === 1) { void command('moves', moves[0]); return true; } return false; };
  const submit = (e: FormEvent) => { e.preventDefault(); const move = typed.trim().toLowerCase(); if (attempt.legalMoves.includes(move)) void command('moves', move); else if (move.length !== 4 || !choose(move.slice(0, 2), move.slice(2, 4))) setError('Enter a legal coordinate move such as e2e4 or a7a8q.'); };
  const styles: Record<string, CSSProperties> = {};
  if (selected && active) { styles[selected] = { backgroundColor: '#bc934e' }; for (const move of attempt.legalMoves.filter(m => m.startsWith(selected))) styles[move.slice(2, 4)] = { backgroundImage: 'radial-gradient(circle, #2b3c4280 22%, transparent 24%)' }; }
  return <><a className="back-link" href="#/puzzles">← Puzzle practice</a><div className="page-heading compact"><div><p className="eyebrow">YOUR MOVE</p><h1>{objective(attempt.objective, attempt.materialTarget)}</h1><p>You play {attempt.side}. {attempt.objective === 'mate' ? 'Complete the mate within three of your moves.' : 'Secure the gain against the checked defensive reply.'}</p></div></div>
    <div className="review-grid training-grid"><section className="board-section" aria-label="Puzzle board"><div className="board-wrap"><Chessboard options={{ id: `puzzle-${attempt.id}`, position: playback === null ? attempt.fen : replay.fen(), boardOrientation: orientation,
      allowDragging: active && !busy, squareStyles: styles, darkSquareStyle: { backgroundColor: '#8d7961' }, lightSquareStyle: { backgroundColor: '#ece2cd' }, animationDurationInMs: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 160,
      onSquareClick: ({ square }) => { if (!active || busy) return; if (selected && choose(selected, square)) return; setSelected(square); }, onPieceDrop: ({ sourceSquare, targetSquare }) => targetSquare ? choose(sourceSquare, targetSquare) : false,
    }} /></div><div className="board-controls"><button onClick={() => setOrientation(o => o === 'white' ? 'black' : 'white')}>Flip board</button><span>{playback === null ? `${attempt.history.length} half-moves played` : `Solution ${playback}/${solution.moves.length}`}</span></div>
      {playback !== null && <><div className="board-controls"><button disabled={playback === 0} onClick={() => setPlayback(n => Math.max(0, (n ?? 0) - 1))}>Previous solution move</button><button disabled={playback === solution.moves.length} onClick={() => setPlayback(n => Math.min(solution.moves.length, (n ?? 0) + 1))}>Next solution move</button><button onClick={() => setPlayback(null)}>Return to attempt</button></div><p className="variation-line" aria-label="Solution notation">{replay.history().join(' ') || 'Starting position'}</p></>}
    </section><div className="review-side"><Panel><h2>{active ? 'Find your continuation' : attempt.state === 'solved' ? 'Objective resolved' : attempt.state === 'failed' ? 'Review this decision' : 'Learn from the solution'}</h2>
      <p role="status" aria-label="Puzzle attempt status">{busy ? 'Checking your move…' : active ? 'Your move. Select a piece and a destination, or use coordinate notation.' : `Attempt ${attempt.state}${attempt.hintsUsed ? ' with assistance' : ''}.`}</p><ErrorNotice message={error} retry={reload} />
      {active && <><form onSubmit={submit}><label>Coordinate move<input value={typed} onChange={e => setTyped(e.target.value)} placeholder="e2e4" maxLength={5} autoComplete="off" disabled={busy} /></label><button className="primary" disabled={busy}>Play move</button></form>
        {promotion && <fieldset className="promotion"><legend>Promote to</legend>{[['q', 'Queen'], ['r', 'Rook'], ['b', 'Bishop'], ['n', 'Knight']].map(([piece, label]) => <button key={piece} disabled={busy} onClick={() => void command('moves', promotion + piece)}>{label}</button>)}<button onClick={() => setPromotion(undefined)}>Cancel</button></fieldset>}
        <div className="analysis-actions"><button disabled={busy} onClick={() => void command('hint')}>Get a hint</button><button disabled={busy} onClick={() => void command('reveal')}>Reveal solution</button></div>{attempt.hint && <p className="practice-note" role="status">{attempt.hint}</p>}<small>{attempt.hintsUsed} hints used. Help changes your review interval.</small></>}
      {!active && <><p>{attempt.hintsUsed} hints · {attempt.solveSeconds ?? 0} seconds from opening to completion, including pauses.</p><p>{due(attempt.reviewDueAt)}</p><button disabled={busy} onClick={() => { setBusy(true); void send<Attempt>(`/puzzles/${attempt.puzzleId}/attempts`).then(a => { location.hash = `/puzzles/attempts/${a.id}`; }).catch(e => setError(e.message)).finally(() => setBusy(false)); }}>Try again</button></>}
    </Panel>{attempt.review && <Panel><p className="eyebrow">CHECKED CONTINUATION</p><h2>Why this works</h2><p>{attempt.review.explanation}</p>{attempt.review.journal.some(m => !m.accepted) && <p>Your move {attempt.review.journal.find(m => !m.accepted)?.move} did not meet the checked objective. Compare the solution below.</p>}
      {solution.decisions.map((node, index) => node.choices.length > 1 && <label key={index}>Accepted alternative at decision {index + 1}<select value={branches[index] ?? 0} onChange={e => { setBranches(b => [...b.slice(0, index), Number(e.target.value)]); setPlayback(0); }}>{node.choices.map((c, i) => <option key={c.move} value={i}>{c.move}</option>)}</select></label>)}
      <button className="primary" onClick={() => setPlayback(0)}>Replay solution</button><p><a href={`#/games/${attempt.review.gameId}?ply=${attempt.review.ply}&run=${attempt.review.runId}`}>Review the original mistake</a></p><small>{attempt.review.engine} · {attempt.review.version}. Difficulty is an estimate, not an official rating.</small>
    </Panel>}</div></div></>;
}
