import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useCallback, useEffect, useState } from 'react';
import { api } from './api';

/** SignalR is a notification channel; HTTP remains the saved source of truth. */
export function useSavedJob<T extends { id: string; version: number }>(path: string, id: string, kind: string) {
  const [saved, setSaved] = useState<{ path: string; value: T }>(); const [error, setError] = useState('');
  const [connected, setConnected] = useState(false); const [revision, setRevision] = useState(0);
  const reload = useCallback(() => setRevision(v => v + 1), []);
  useEffect(() => {
    let disposed = false; let fetching = false; let dirty = false; let version = -1; let delay = 1000;
    let poll: ReturnType<typeof setTimeout>; let retry: ReturnType<typeof setTimeout>;
    const controller = new AbortController();
    const fetchState = async () => {
      if (disposed) return;
      if (fetching) { dirty = true; return; }
      fetching = true;
      do {
        dirty = false;
        try {
          const result = await api<T>(path, { signal: controller.signal });
          if (result.id !== id) throw new Error('The returned job does not match this request.');
          if (!disposed) { setSaved(old => old?.path === path && old.value.version > result.version ? old : { path, value: result }); setError(''); }
        } catch (e) { if (!disposed) setError((e as Error).message); }
      } while (dirty && !disposed);
      fetching = false;
    };
    const reconcile = async () => { await fetchState(); if (!disposed) poll = setTimeout(reconcile, 5000); };
    const hub = new HubConnectionBuilder().withUrl('/hubs/progress').withAutomaticReconnect([0, 1000, 3000, 10000]).configureLogging(LogLevel.Error).build();
    hub.on('jobProgress', (event: { kind: string; id: string; version: number }) => {
      if (disposed || event.kind !== kind || event.id !== id || event.version <= version) return;
      version = event.version; void fetchState();
    });
    const connect = async () => {
      if (disposed) return;
      try { await hub.start(); if (disposed) { await hub.stop(); return; } await hub.invoke('SubscribeJob', id); if (disposed) return; setConnected(true); delay = 1000; void fetchState(); }
      catch { if (!disposed) { setConnected(false); retry = setTimeout(connect, delay); delay = Math.min(30000, delay * 2); } }
    };
    hub.onreconnecting(() => { if (!disposed) setConnected(false); });
    hub.onreconnected(() => { if (!disposed) { setConnected(true); void fetchState(); } });
    hub.onclose(() => { if (!disposed) { setConnected(false); retry = setTimeout(connect, delay); } });
    setConnected(false); void reconcile(); void connect();
    return () => { disposed = true; controller.abort(); clearTimeout(poll); clearTimeout(retry); void hub.stop().catch(() => {}); };
  }, [path, id, kind, revision]);
  return { data: saved?.path === path ? saved.value : undefined, error, reload, connected };
}
