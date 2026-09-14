export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message); }
}
let csrfToken: string | undefined;
export async function refreshCsrf() {
  const res = await fetch('/api/account/csrf', { credentials: 'same-origin' });
  if (!res.ok) throw new ApiError('Unable to establish a secure session. Please retry.', res.status);
  csrfToken = (await res.json()).token;
}
export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = options.method ?? 'GET';
  if (method !== 'GET' && !csrfToken) await refreshCsrf();
  const headers = new Headers(options.headers);
  if (options.body) headers.set('Content-Type', 'application/json');
  if (method !== 'GET') headers.set('X-CSRF-TOKEN', csrfToken!);
  const res = await fetch(`/api${path}`, { ...options, headers, credentials: 'same-origin' });
  if (!res.ok) {
    const problem = await res.json().catch(() => ({}));
    const errors = problem.errors ? Object.values(problem.errors).flat().join(' ') : undefined;
    if (res.status === 401 && path !== '/account/login') window.dispatchEvent(new Event('session-expired'));
    throw new ApiError(problem.detail ?? errors ?? problem.title ?? `Request failed (${res.status}). Please retry.`, res.status);
  }
  return res.status === 204 ? undefined as T : res.json();
}
export function send<T>(path: string, data?: unknown, method = 'POST') {
  return api<T>(path, { method, body: data === undefined ? undefined : JSON.stringify(data) });
}
export type User = { id: string; email: string; timezone: string; coachingLevel: string; preferredTimeControls: string; isDemo: boolean };
export type GameSummary = { id: string; white: string; black: string; whiteRating: number | null; blackRating: number | null; result: string; playerColour: 'white' | 'black' | null; playedAt: string | null; provider: string; timeControl: string | null; opening: string | null; plies: number };
export type Move = { ply: number; san: string; uci: string; fenBefore: string; fenAfter: string; side: string; clockSeconds: number | null };
export type Game = GameSummary & { initialFen: string; moves: Move[]; rawPgn: string; termination: string | null };
export type Page<T> = { items: T[]; total: number; page: number; pageSize: number };
export type Job = { id: string; state: string; imported: number; duplicates: number; processed: number; total: number; failures: { gameNumber: number; message: string }[]; error: string | null; version: number; cancelRequested: boolean };
export type Position = { fen: string; sideToMove: string; legalMoves: string[]; endgame: string | null };
