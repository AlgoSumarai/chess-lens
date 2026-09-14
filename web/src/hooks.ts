import { useCallback, useEffect, useState } from 'react';
import { api } from './api';

export function useRemote<T>(path: string, interval?: number) {
  const [data, setData] = useState<T>();
  const [error, setError] = useState('');
  const [version, setVersion] = useState(0);
  const reload = useCallback(() => setVersion(v => v + 1), []);
  useEffect(() => {
    let controller: AbortController;
    let live = true;
    let timer: ReturnType<typeof setTimeout>;
    setData(undefined);
    const load = async () => {
      controller = new AbortController();
      try { const result = await api<T>(path, { signal: controller.signal }); if (live) { setData(result); setError(''); } }
      catch (e) { if (live && !(e instanceof DOMException && e.name === 'AbortError')) setError((e as Error).message); }
      if (live && interval) timer = setTimeout(load, interval);
    };
    void load();
    return () => { live = false; controller?.abort(); clearTimeout(timer); };
  }, [path, version, interval]);
  return { data, error, reload };
}

export function useRoute() {
  const [route, setRoute] = useState(location.hash.slice(1) || '/');
  useEffect(() => { const change = () => setRoute(location.hash.slice(1) || '/'); window.addEventListener('hashchange', change); return () => window.removeEventListener('hashchange', change); }, []);
  return route;
}
