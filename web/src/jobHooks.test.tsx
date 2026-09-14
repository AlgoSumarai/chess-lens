import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { api } from './api';
import { useSavedJob } from './jobHooks';

const events = vi.hoisted(() => ({ receivers: [] as ((event: { kind: string; id: string; version: number }) => void)[] }));
vi.mock('./api', () => ({ api: vi.fn() }));
vi.mock('@microsoft/signalr', () => ({ LogLevel: { Error: 0 }, HubConnectionBuilder: class {
  withUrl() { return this; } withAutomaticReconnect() { return this; } configureLogging() { return this; }
  build() { return { on: (_name: string, callback: (event: { kind: string; id: string; version: number }) => void) => events.receivers.push(callback),
    start: async () => {}, stop: async () => {}, invoke: async () => {}, onreconnecting: () => {}, onreconnected: () => {}, onclose: () => {} }; }
} }));
afterEach(() => { vi.clearAllMocks(); events.receivers.length = 0; });

test('late completion of a previous puzzle and lower-version HTTP state cannot replace the current puzzle', async () => {
  vi.mocked(api).mockResolvedValue({ id: 'a', version: 1, state: 'running' });
  const hook = renderHook(({ id }) => useSavedJob<{ id: string; version: number; state: string }>(`/puzzles/${id}`, id, 'puzzle'), { initialProps: { id: 'a' } });
  await waitFor(() => expect(hook.result.current.data?.state).toBe('running'));
  let resolveOld: (value: unknown) => void = () => {};
  vi.mocked(api).mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
  await act(async () => events.receivers[0]({ kind: 'puzzle', id: 'a', version: 2 }));
  vi.mocked(api).mockResolvedValue({ id: 'b', version: 5, state: 'ready' });
  hook.rerender({ id: 'b' }); await waitFor(() => expect(hook.result.current.data?.id).toBe('b'));
  await act(async () => resolveOld({ id: 'a', version: 9, state: 'failed' }));
  expect(hook.result.current.data).toEqual({ id: 'b', version: 5, state: 'ready' });
  vi.mocked(api).mockResolvedValue({ id: 'b', version: 4, state: 'running' });
  await act(async () => events.receivers[1]({ kind: 'puzzle', id: 'b', version: 6 }));
  expect(hook.result.current.data?.state).toBe('ready');
  const calls = vi.mocked(api).mock.calls.length;
  await act(async () => events.receivers[1]({ kind: 'puzzle', id: 'b', version: 5 }));
  expect(vi.mocked(api).mock.calls).toHaveLength(calls);
  hook.unmount();
});
