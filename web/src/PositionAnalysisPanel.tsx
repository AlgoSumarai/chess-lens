import { useEffect, useRef, useState } from 'react';
import { Chess } from 'chess.js';
import { api, send } from './api';
import { type Score } from './analysis';
import { ErrorNotice, Panel } from './App';

type PositionJob = { id: string; fen: string; sequence: number; state: string; version: number; error: string | null;
  result: null | { engine: string; lines: { rank: number; depth: number; score: Score; pv: string[]; bound: boolean }[] } };

export function PositionAnalysisPanel({ gameId, ply, variation, fen, ready, revision }: {
  gameId: string; ply: number; variation: string[]; fen: string; ready: boolean; revision: number;
}) {
  const [enabled, setEnabled] = useState(false);
  const [channel] = useState(() => crypto.randomUUID());
  const sequence = useRef(0);
  const [job, setJob] = useState<PositionJob>();
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  const variationKey = variation.join(' ');
  useEffect(() => {
    setJob(undefined); setError('');
    if (!enabled || !ready) return;
    let disposed = false;
    const requestSequence = ++sequence.current;
    const timer = setTimeout(() => {
      void send<PositionJob>(`/games/${gameId}/position-analysis`, { channelId: channel, sequence: requestSequence, ply, variation: variationKey ? variationKey.split(' ') : [] })
        .then(value => { if (!disposed && sequence.current === requestSequence) setJob(value); })
        .catch(e => { if (!disposed) setError(e.message); });
    }, 350);
    return () => {
      disposed = true; clearTimeout(timer);
      // A persisted sequence barrier also rejects an older POST that arrives
      // after this cancellation; aborting fetch alone cannot provide that guarantee.
      void send(`/games/${gameId}/position-analysis`, { channelId: channel, sequence: ++sequence.current, ply: 0, variation: [], cancelOnly: true }).catch(() => {});
    };
  }, [enabled, ready, gameId, channel, ply, variationKey, retry]);

  const jobId = job?.id;
  const active = job?.state === 'queued' || job?.state === 'running';
  useEffect(() => {
    if (!jobId || !active) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    const refresh = async () => {
      try {
        const result = await api<PositionJob>(`/position-analysis/${jobId}`, { signal: controller.signal });
        if (!controller.signal.aborted) setJob(old => old?.id === result.id && old.version <= result.version ? result : old);
      } catch (e) { if (!controller.signal.aborted) setError((e as Error).message); }
      if (!controller.signal.aborted) timer = setTimeout(refresh, 2000);
    };
    void refresh();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [jobId, active, revision]);

  const current = enabled && ready && job?.fen === fen && job.sequence === sequence.current ? job : undefined;
  const lines = current?.result?.lines ?? [];
  const side = fen.split(' ')[1] === 'w' ? 'White' : 'Black';
  const notation = (pv: string[]) => {
    try { const board = new Chess(fen); for (const uci of pv.slice(0, 10)) board.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] }); return board.history().join(' '); }
    catch { return 'Continuation unavailable.'; }
  };
  const label = (score: Score) => score.kind === 'mate'
    ? `${score.value > 0 ? side : side === 'White' ? 'Black' : 'White'} mates in ${Math.abs(score.value)}`
    : `${score.value >= 0 ? '+' : ''}${(score.value / 100).toFixed(2)} for ${side}`;
  return <Panel className="position-analysis"><div className="section-title"><h2>Explore this position</h2><span className="pill">POST-GAME</span></div>
    <p>Compare candidate moves in the position on your board. Estimates use a bounded search and may change with deeper analysis.</p>
    <button disabled={!ready && !enabled} onClick={() => setEnabled(v => !v)}>{enabled ? 'Stop position analysis' : 'Analyse this position'}</button>
    {enabled && <p role="status" aria-label="Position analysis status">{!ready ? 'Waiting for a legal position.' : error ? 'Position review needs attention.' : !current ? 'Preparing position review…' : current.state === 'completed' ? `Position reviewed · ${current.result?.engine}` : current.state === 'queued' ? 'Position review queued…' : current.state === 'running' ? 'Analysing the selected position…' : `Position review ${current.state}.`}</p>}
    <ErrorNotice message={error || current?.error || ''} retry={() => setRetry(v => v + 1)} />
    {lines.length > 0 && <div aria-label="Candidate continuations">{lines.map(line => <div className="coaching" key={line.rank}>
      <strong>Candidate {line.rank} · {label(line.score)}{line.bound ? ' · bound' : ''}</strong>
      <p className="variation-line">{notation(line.pv)}</p><small>Depth {line.depth} · provisional position estimate</small>
    </div>)}</div>}
  </Panel>;
}
