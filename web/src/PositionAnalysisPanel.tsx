import { StatusMessage } from './StatusMessage';
import type { Score } from './analysis';
import type { LivePositionAnalysis } from './positionAnalysis';
import { ErrorNotice, Panel } from './App';

export function PositionAnalysisPanel({ analysis, fen, onPlay }: {
  analysis: LivePositionAnalysis; fen: string; onPlay: (uci: string) => boolean;
}) {
  const { enabled, toggle, current, candidates, ready, terminal, error, retry } = analysis;
  const side = fen.split(' ')[1] === 'w' ? 'White' : 'Black';
  const label = (score: Score) => score.kind === 'mate'
    ? `${score.value > 0 ? side : side === 'White' ? 'Black' : 'White'} mates in ${Math.abs(score.value)}`
    : `${score.value >= 0 ? '+' : ''}${(score.value / 100).toFixed(2)} for ${side}`;
  return <Panel className="position-analysis"><div className="section-title"><h2>Explore this position</h2><span className="pill">POST-GAME</span></div>
    <p>Enable analysis to follow the board as you replay or explore. Select a candidate to play its first move.</p>
    <button aria-pressed={enabled} onClick={toggle}>{enabled ? 'Stop position analysis' : 'Analyse this position'}</button>
    {enabled && <StatusMessage aria-label="Position analysis status">{!ready ? 'Checking the selected position…' : terminal ? 'No legal moves. This position has ended.' : error ? 'Position review needs attention.' : !current ? 'Preparing position review…' : current.state === 'completed' ? `Position reviewed · ${current.result?.engine}` : current.state === 'queued' ? 'Position review queued…' : current.state === 'running' ? 'Analysing the selected position…' : `Position review ${current.state}.`}</StatusMessage>}
    <ErrorNotice message={error} retry={retry} />
    {enabled && <div role="group" aria-label="Candidate continuations" aria-busy={analysis.pending} className="candidate-list">{candidates.map(line => <button type="button" className="candidate" key={line.rank} aria-label={`Play candidate ${line.rank}: ${line.san[0]}`} onClick={() => onPlay(line.pv[0])}>
      <strong>Candidate {line.rank} · {label(line.score)}{line.bound ? ' · bound' : ''}</strong>
      <span className="variation-line">{line.san.join(' ')}</span><small>Depth {line.depth} · provisional position estimate</small>
    </button>)}</div>}
  </Panel>;
}
