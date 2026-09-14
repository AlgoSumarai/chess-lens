import { useState } from 'react';
import { Chess } from 'chess.js';
import { send, type Game } from './api';
import { scoreLabel, whiteScore, type Analysis, type MoveAnalysis } from './analysis';
import { ErrorNotice, Panel } from './App';
import { CreatePuzzleButton } from './Puzzles';

export function AnalysisPanel({ game, run, current, exploring, error, reload, connected, readOnly = false }: { game: Game; run: Analysis | null | undefined; current?: MoveAnalysis; exploring: boolean; error: string; reload: () => void; connected: boolean; readOnly?: boolean }) {
  const [pending, setPending] = useState(false); const [problem, setProblem] = useState('');
  const queue = async (profile: string) => {
    setPending(true); setProblem('');
    try { await send(`/games/${game.id}/analysis`, { profile }); reload(); } catch (e) { setProblem((e as Error).message); } finally { setPending(false); }
  };
  const active = run && ['queued', 'running'].includes(run.state);
  let bestSan = current?.bestMove;
  let line = '';
  if (current) {
    try { const board = new Chess(game.moves[current.ply - 1].fenBefore); for (const uci of current.pv.slice(0, 8)) board.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] }); bestSan = board.history()[0]; line = board.history().join(' '); } catch { line = 'Variation unavailable.'; }
  }
  return <Panel className="engine-panel"><div className="section-title"><h2>Engine review</h2>{run?.engineVersion && <span className="pill">{run.engineVersion}</span>}</div><ErrorNotice message={error || problem} retry={reload} />
    {!run && <p>See the moments that changed this game. Quick reviews are provisional; critical positions receive a deeper check.</p>}
    {active && <><progress max={run.totalPlies || 1} value={run.completedPlies} aria-label="Analysis progress" /><p role="status" aria-label="Analysis status">{run.state === 'queued' ? 'Waiting for an engine…' : `Reviewed ${run.completedPlies} of ${run.totalPlies} half-moves.`} {connected ? 'Live updates connected.' : 'Reconnecting; progress is saved.'}</p><button disabled={run.cancelRequested} onClick={() => void send(`/analysis/${run.id}/cancel`).then(reload).catch(e => setProblem(e.message))}>{run.cancelRequested ? 'Cancelling…' : 'Cancel analysis'}</button></>}
    {run?.error && <p role="status">{run.error}</p>}
    {run && !active && <p className="metadata">{run.state} · {run.profile} review · {run.completedPlies}/{run.totalPlies} half-moves</p>}
    {exploring ? <p>Return to the mainline to see this game's recorded evaluation.</p> : current ? <div className="coaching"><span className={`classification ${current.classification}`}>{current.classification}{current.provisional ? ' · provisional' : ''}</span><h3>{current.classification === 'sound' ? 'The position holds up.' : `Consider ${bestSan}.`}</h3><p>{current.centipawnLoss !== null ? `${game.moves[current.ply - 1].san} changed the evaluation by ${(current.centipawnLoss / 100).toFixed(2)} pawns against the moving player, compared with the engine's best continuation.` : current.mateTransition?.replaceAll('-', ' ')}</p><p className="variation-line">{line}</p><small>ChessLens classification · depth {current.depth}. {current.provisional ? 'Treat this as an early indication.' : 'Critical losses were checked with deeper analysis.'}</small></div> : run && <p>Select an analysed move to inspect its evaluation and best continuation.</p>}
    {!active && !readOnly && <div className="analysis-actions"><button className="primary" disabled={pending} onClick={() => void queue('quick')}>{pending ? 'Queuing…' : run ? 'Run quick review again' : 'Analyse game'}</button><button disabled={pending} onClick={() => void queue('deep')}>Deep review</button></div>}
    {!exploring && run?.state === 'completed' && current?.complete && !current.provisional && current.side === game.playerColour && (current.centipawnLoss !== null && current.centipawnLoss >= 100 || current.mateTransition === 'missed-forced-mate' || current.mateTransition === 'allowed-forced-mate') && <CreatePuzzleButton runId={run.id} ply={current.ply} />}
  </Panel>;
}

export function EvaluationGraph({ run, ply, onSelect }: { run: Analysis; ply: number; onSelect: (ply: number) => void }) {
  const value = (move: MoveAnalysis) => { const score = whiteScore(move); return score.kind === 'mate' ? Math.sign(score.value) : Math.tanh(score.value / 400); };
  const x = (n: number) => 12 + (n / Math.max(1, run.totalPlies)) * 576;
  const y = (move: MoveAnalysis) => 70 - value(move) * 53;
  return <section className="evaluation-graph" aria-label="Game evaluation"><div className="section-title"><h3>How the position changed</h3><small>White's perspective</small></div><svg viewBox="0 0 600 140" role="img" aria-label="Evaluation by move. Select a move using the buttons below."><line x1="12" x2="588" y1="70" y2="70" stroke="#53616f" strokeDasharray="4 4" /><polyline fill="none" stroke="#e5b76a" strokeWidth="2" points={run.moves.map(m => `${x(m.ply)},${y(m)}`).join(' ')} />{run.moves.map(m => <circle key={m.ply} cx={x(m.ply)} cy={y(m)} r={m.ply === ply ? 5 : 3} fill={m.ply === ply ? '#fff4dc' : '#e5b76a'} onClick={() => onSelect(m.ply)}><title>Half-move {m.ply}: {scoreLabel(whiteScore(m))}</title></circle>)}</svg><div className="graph-points">{run.moves.map(m => <button key={m.ply} aria-label={`Evaluation at half-move ${m.ply}: ${scoreLabel(whiteScore(m))}`} aria-pressed={m.ply === ply} onClick={() => onSelect(m.ply)}>{m.ply}<small>{scoreLabel(whiteScore(m))}</small></button>)}</div><small>Centipawn scale is compressed for readability. Mate scores remain distinct; this is not a win probability.</small></section>;
}

export function EvaluationBar({ move }: { move: MoveAnalysis }) {
  const score = whiteScore(move);
  const width = score.kind === 'mate' ? score.value > 0 ? 100 : 0 : 50 + Math.tanh(score.value / 400) * 45;
  return <div className="evaluation-bar" role="img" aria-label={`White's evaluation: ${scoreLabel(score)}`}><div style={{ width: `${width}%` }} /><span>{scoreLabel(score)}{move.provisional ? ' · provisional' : ''}</span></div>;
}
