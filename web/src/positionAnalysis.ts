import { useEffect, useMemo, useRef, useState } from 'react';
import { Chess } from 'chess.js';
import { api, send } from './api';
import type { Score } from './analysis';

export type PositionJob = { id: string; fen: string; sequence: number; state: string; version: number; error: string | null;
  result: null | { engine: string; lines: { rank: number; depth: number; score: Score; pv: string[]; bound: boolean }[] } };

export function usePositionAnalysis({ gameId, ply, variation, fen, ready, legalMoves, revision }: {
  gameId: string; ply: number; variation: string[]; fen: string; ready: boolean; legalMoves: string[]; revision: number;
}) {
  const [enabled, setEnabled] = useState(false);
  const [channel] = useState(() => crypto.randomUUID());
  const sequence = useRef(0);
  const [response, setResponse] = useState<{ key: string; job: PositionJob }>();
  const [problem, setProblem] = useState<{ key: string; sequence: number; message: string }>();
  const [retry, setRetry] = useState(0);
  const variationKey = variation.join(' ');
  const key = JSON.stringify([gameId, ply, variationKey, fen, ready, retry]);
  const terminal = ready && legalMoves.length === 0;
  useEffect(() => {
    if (!enabled || !ready || terminal) return;
    let disposed = false;
    const requestSequence = ++sequence.current;
    setResponse(undefined); setProblem(undefined);
    const timer = setTimeout(() => {
      void send<PositionJob>(`/games/${gameId}/position-analysis`, { channelId: channel, sequence: requestSequence, ply, variation: variationKey ? variationKey.split(' ') : [] })
        .then(job => { if (!disposed && sequence.current === requestSequence) setResponse({ key, job }); })
        .catch(e => { if (!disposed) setProblem({ key, sequence: requestSequence, message: e.message }); });
    }, 350);
    return () => {
      disposed = true; clearTimeout(timer);
      // Persist a sequence barrier: even an older POST arriving after cancellation is rejected.
      void send(`/games/${gameId}/position-analysis`, { channelId: channel, sequence: ++sequence.current, ply: 0, variation: [], cancelOnly: true }).catch(() => {});
    };
  }, [enabled, ready, terminal, gameId, channel, ply, variationKey, key]);

  // Check identity during render as well as in async callbacks. Navigation must hide
  // old scores immediately, before the next request's effect has run (including A → B → A).
  const current = enabled && ready && !terminal && response?.key === key
    && response.job.fen === fen && response.job.sequence === sequence.current ? response.job : undefined;
  const jobId = current?.id;
  const jobSequence = current?.sequence;
  const active = current?.state === 'queued' || current?.state === 'running';
  useEffect(() => {
    if (!jobId || !active) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    const refresh = async () => {
      try {
        const job = await api<PositionJob>(`/position-analysis/${jobId}`, { signal: controller.signal });
        if (!controller.signal.aborted) {
          setResponse(old => old?.key === key && old.job.id === job.id && old.job.sequence === job.sequence && old.job.version <= job.version ? { key, job } : old);
          setProblem(old => old?.key === key ? undefined : old);
        }
      } catch (e) { if (!controller.signal.aborted) setProblem({ key, sequence: jobSequence!, message: (e as Error).message }); }
      if (!controller.signal.aborted) timer = setTimeout(refresh, 2000);
    };
    void refresh();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [jobId, jobSequence, active, revision, key]);

  const candidates = useMemo(() => (current?.state === 'completed' ? current.result?.lines ?? [] : [])
    .slice().sort((a, b) => a.rank - b.rank).slice(0, 3).flatMap(line => {
      const board = new Chess(fen);
      const san: string[] = [];
      for (const uci of line.pv.slice(0, 10)) {
        try { san.push(board.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] }).san); }
        catch { break; }
      }
      return san.length ? [{ ...line, san }] : [];
    }), [current, fen]);
  const error = enabled && ready && !terminal ? (problem?.key === key && problem.sequence === sequence.current ? problem.message : '') || current?.error || '' : '';
  const best = candidates[0]?.score;
  const whiteScore = best ? { ...best, value: fen.split(' ')[1] === 'w' ? best.value : -best.value } : undefined;
  return { enabled, toggle: () => setEnabled(v => !v), current, candidates, whiteScore, error,
    pending: enabled && !terminal && !error && (!current || active), terminal, ready,
    retry: () => setRetry(v => v + 1) };
}

export type LivePositionAnalysis = ReturnType<typeof usePositionAnalysis>;
