import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useCallback, useEffect, useState } from 'react';
import { api } from './api';

export type Score = { kind: 'cp' | 'mate'; value: number };
export type MoveAnalysis = { ply: number; side: string; playedMove: string; bestMove: string; bestScore: Score; playedScore: Score;
  centipawnLoss: number | null; mateTransition: string | null; classification: string; provisional: boolean; complete: boolean; pv: string[]; depth: number; nodes: number };
export type Analysis = { id: string; gameId: string; state: string; profile: string; analysisVersion: string; engineVersion: string | null; completedPlies: number; totalPlies: number;
  version: number; cancelRequested: boolean; error: string | null; moves: MoveAnalysis[] };

export function useAnalysis(gameId: string, runId?: string) {
  const [data, setData] = useState<Analysis | null>();
  const [error, setError] = useState('');
  const [connected, setConnected] = useState(false);
  const [revision, setRevision] = useState(0);
  const [positionRevision, setPositionRevision] = useState(0);
  const reload = useCallback(() => setRevision(r => r + 1), []);
  useEffect(() => {
    let disposed = false;
    const controller = new AbortController();
    let poll: ReturnType<typeof setTimeout>;
    let retry: ReturnType<typeof setTimeout>;
    let delay = 1000;
    const versions = new Map<string, number>();
    const fetchState = async () => {
      try {
        const result = await api<Analysis | null>(runId ? `/analysis/${runId}` : `/games/${gameId}/analysis`, { signal: controller.signal });
        if (result && result.gameId !== gameId) throw new Error('This saved review belongs to another game.');
        if (!disposed) { setData(old => old && result && old.id === result.id && old.version > result.version ? old : result); setError(''); }
      } catch (e) { if (!disposed) setError((e as Error).message); }
    };
    const reconcile = async () => { await fetchState(); if (!disposed) poll = setTimeout(reconcile, 5000); };
    const hub = new HubConnectionBuilder().withUrl('/hubs/progress').withAutomaticReconnect([0, 1000, 3000, 10000]).configureLogging(LogLevel.Error).build();
    hub.on('jobProgress', (event: { kind: string; id: string; gameId: string; version: number }) => {
      if (event.kind === 'position' && event.gameId === gameId && (versions.get(event.id) ?? -1) < event.version) {
        versions.set(event.id, event.version); setPositionRevision(v => v + 1); return;
      }
      if (event.kind !== 'analysis' || event.gameId !== gameId || (versions.get(event.id) ?? -1) >= event.version) return;
      versions.set(event.id, event.version); void fetchState();
    });
    const connect = async () => {
      if (disposed) return;
      try { await hub.start(); if (disposed) { await hub.stop(); return; } setConnected(true); delay = 1000; await fetchState(); }
      catch { if (!disposed) { setConnected(false); retry = setTimeout(connect, delay); delay = Math.min(30000, delay * 2); } }
    };
    hub.onreconnecting(() => { if (!disposed) setConnected(false); });
    hub.onreconnected(() => { if (!disposed) { setConnected(true); void fetchState(); } });
    hub.onclose(() => { if (!disposed) { setConnected(false); retry = setTimeout(connect, delay); } });
    void reconcile(); void connect();
    return () => { disposed = true; controller.abort(); clearTimeout(poll); clearTimeout(retry); void hub.stop(); };
  }, [gameId, runId, revision]);
  return { data, error, reload, connected, positionRevision };
}

export function whiteScore(move: MoveAnalysis): Score { return { ...move.playedScore, value: move.side === 'white' ? move.playedScore.value : -move.playedScore.value }; }
export function scoreLabel(score: Score) { return score.kind === 'mate' ? `${score.value < 0 ? 'Black' : 'White'} mates in ${Math.abs(score.value)}` : `${score.value >= 0 ? '+' : ''}${(score.value / 100).toFixed(2)}`; }
