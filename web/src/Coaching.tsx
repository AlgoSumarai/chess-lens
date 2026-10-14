import { StatusMessage } from './StatusMessage';
import { useCallback, useEffect, useState } from 'react';
import { api, send } from './api';
import { ErrorNotice } from './App';

type Explanation = { evidenceId: string; runId: string; ply: number; fen: string; level: string;
  rating: { provider: string; pool: string | null; timeControl: string | null; rating: number | null; presentationAdjusted: boolean };
  whatHappened: string; whyItMattered: string; betterLine: string[]; sanLine: string[]; lesson: string;
  mode: 'factual' | 'model-selected'; fallbackReason: string | null; promptVersion: string; model: string | null };
type CoachingJob = { id: string; runId: string; ply: number; state: string; version: number; error: string | null; result: Explanation | null };

export function Coaching({ runId, ply, fen, level, modelOptIn }: { runId: string; ply: number; fen: string; level: string; modelOptIn: boolean }) {
  const key = `${runId}:${ply}:${fen}:${level}:${modelOptIn}`;
  const [saved, setSaved] = useState<{ key: string; value: CoachingJob }>(); const [error, setError] = useState('');
  const [retry, setRetry] = useState(0); const reload = useCallback(() => setRetry(r => r + 1), []);
  useEffect(() => {
    let disposed = false; let jobId: string | undefined; let timer: ReturnType<typeof setTimeout>; let version = -1;
    const controller = new AbortController(); setError('');
    const accept = (job: CoachingJob) => {
      if (job.runId !== runId || job.ply !== ply || job.result && (job.result.fen !== fen || job.result.level !== level))
        throw new Error('This explanation does not match the selected decision.');
      if (!disposed && job.version >= version) { version = job.version; setSaved({ key, value: job }); }
    };
    const poll = async () => {
      try {
        const job = await api<CoachingJob>(`/coaching/${jobId}`, { signal: controller.signal }); accept(job);
        if (!disposed && ['queued', 'running'].includes(job.state)) timer = setTimeout(poll, 1500);
      } catch (e) { if (!disposed) setError((e as Error).message); }
    };
    void api<CoachingJob>('/coaching', { method: 'POST', body: JSON.stringify({ runId, ply }), signal: controller.signal }).then(job => {
      jobId = job.id;
      if (disposed) { if (['queued', 'running'].includes(job.state)) void send(`/coaching/${job.id}/cancel`).catch(() => {}); return; }
      accept(job); if (['queued', 'running'].includes(job.state)) timer = setTimeout(poll, 1500);
    }).catch(e => { if (!disposed) setError((e as Error).message); });
    return () => {
      disposed = true; controller.abort(); clearTimeout(timer);
      if (jobId) void send(`/coaching/${jobId}/cancel`).catch(() => {});
    };
  }, [runId, ply, fen, level, modelOptIn, key, retry]);
  const job = saved?.key === key ? saved.value : undefined;
  const explanation = job?.result;
  return <section className="grounded-coaching" aria-label="Saved decision coaching">
    <div className="section-title"><h3>Your next lesson</h3>{explanation && <span className="pill">{explanation.mode === 'factual' ? 'Factual coaching' : 'Model-selected coaching'}</span>}</div>
    <ErrorNotice message={error || job?.error || ''} retry={reload} />
    {!explanation && !error && !job?.error && <StatusMessage>{job?.state === 'cancelled' ? 'Explanation stopped. Reopen this decision or retry.' : 'Preparing the explanation for this saved decision…'}</StatusMessage>}
    {job?.state === 'cancelled' && <button onClick={reload}>Retry explanation</button>}
    {explanation && <>
      <p><strong>What happened.</strong> {explanation.whatHappened}</p>
      <p><strong>Why it mattered.</strong> {explanation.whyItMattered}</p>
      <p><strong>A better continuation.</strong> <span className="variation-line">{explanation.sanLine.join(' ')}</span></p>
      <small>This alternative starts before the selected move. Previous returns to that decision position.</small>
      <p className="practice-note"><strong>Try this next time.</strong> {explanation.lesson}</p>
      <small>{explanation.level} level · saved review, half-move {explanation.ply}.
        {explanation.rating.rating !== null && <> Recorded {explanation.rating.provider === 'chesscom' ? 'Chess.com' : explanation.rating.provider === 'lichess' ? 'Lichess' : 'PGN'} rating: {explanation.rating.rating}
          {explanation.rating.pool ? ` · ${explanation.rating.pool}` : ' · pool unavailable'}{explanation.rating.timeControl ? ` · ${explanation.rating.timeControl}` : ''}.</>}
        {explanation.mode === 'model-selected' && ' The model selected from verified facts and lessons; free-form claims are not displayed.'}
        {explanation.fallbackReason && ' Factual coaching is available while the optional model explanation is unavailable.'}
      </small>
    </>}
  </section>;
}
