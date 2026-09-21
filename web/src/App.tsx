import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { api, refreshCsrf, send, type GameSummary, type Job, type Page, type User } from './api';
import { useRemote, useRoute } from './hooks';
import { GameReview } from './GameReview';
import { Weaknesses, type InsightSnapshot } from './Weaknesses';
import { PuzzleLibrary, PuzzlePage, PuzzleAttemptPage } from './Puzzles';

export function ErrorNotice({ message, retry }: { message: string; retry?: () => void }) {
  return message ? <div className="error" role="alert">{message}{retry && <button onClick={retry}>Retry</button>}</div> : null;
}
export function Panel({ children, className = '' }: { children: ReactNode; className?: string }) { 
  return <section className={`panel ${className}`}>{children}</section>; 
}
const date = (value: string | null) => value ? new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : 'Date unavailable';

export function App() {
  const [user, setUser] = useState<User | null>();
  const [error, setError] = useState('');
  const route = useRoute();
  const routePath = route.split('?')[0];
  const routeQuery = new URLSearchParams(route.split('?')[1] ?? '');

  //user-handling ---start
  const loadAccount = async () => {
    try { 
      const u = await api<User>('/account/me'); 
      await refreshCsrf(); setUser(u); 
      setError(''); 
    } catch (e) { 
      if ((e as { status?: number }).status === 401) 
        setUser(null); else setError((e as Error).message); 
    }
  };

  useEffect(() => { 
    void loadAccount(); 
    const expired = () => setUser(null); 
    window.addEventListener('session-expired', expired); 
    return () => window.removeEventListener('session-expired', expired); 
  }, []);

  if (error) 
    return <main className="auth-wrap"><ErrorNotice message={error} retry={() => void loadAccount()} /></main>;
  if (user === undefined) 
    return <main className="auth-wrap" aria-busy="true">Opening your workspace…</main>;
  if (user === null) 
    return <Auth onComplete={() => void loadAccount()} />;

  const links = [['/', 'Overview', '◈'], 
    ['/games', 'Your games', '▦'], 
    ['/import', 'Import games', '＋'], 
    ['/settings', 'Settings', '⚙']];

  links.splice(2, 0, ['/weaknesses', 'Weaknesses', '◎']);
  links.splice(3, 0, ['/puzzles', 'Puzzle practice', '♙']);

  return <div className="shell">
    <a className="skip" href="#main" onClick={event => { 
      event.preventDefault(); 
      document.getElementById('main')?.focus();
       }}>Skip to content</a>

    <aside className="sidebar">
      <a href="#/" className="brand">
        <span className="brand-icon" aria-hidden="true">♞</span>Chess<span>Lens</span>
      </a>
      <div className="nav-label">YOUR WORKSPACE</div>
      <nav aria-label="Main navigation">{links.map(([path, label, icon]) => 
        <a key={path} href={`#${path}`} aria-current={routePath === path || path !== '/' && routePath.startsWith(path + '/') ? 'page' : undefined}>
          <span aria-hidden="true">{icon}</span>{label}</a>)}
      </nav>
      <div className="sidebar-bottom">
        <span className="avatar">{user.email[0].toUpperCase()}</span>
        <div><strong>{user.isDemo ? 'Demo workspace' : 'Your chess journey'}</strong><small>{user.email}</small></div>
      </div>
    </aside>
    <div className="content">
      <header className="topbar"><span>Review. Understand. Improve.</span><span className="level">{user.coachingLevel} coaching</span></header>
      
      {/* linking routes to their components */}
      <main id="main" tabIndex={-1}>
        { 
          route === '/' ? <Dashboard /> :
          route === '/games' ? <Library /> : 
          route === '/import' ? <Import /> : 
          route === '/weaknesses' ? <Weaknesses /> : 
          route === '/puzzles' ? <PuzzleLibrary /> : 
          routePath.startsWith('/puzzles/attempts/') ? <PuzzleAttemptPage id={routePath.split('/')[3]} key={route} /> : 
          routePath.startsWith('/puzzles/') ? <PuzzlePage id = {routePath.split('/')[2]} key = {route} /> : 
          routePath.startsWith('/games/') ? <GameReview id = {routePath.split('/')[2]} initialPly = {Number(routeQuery.get('ply') ?? 0)} runId = {routeQuery.get('run') ?? undefined} key={route} /> :
          route === '/settings' ? <Settings user={user} onUpdate={setUser} onSignOut={() => setUser(null)} /> : 
          <Panel><h1>Page not found</h1><a href="#/">Return to overview</a></Panel>
        }
      </main>
      <footer>ChessLens · Completed games. Deliberate practice.</footer>
    </div>
  </div>;
}


function Auth({ onComplete }: { onComplete: () => void }) {
  const [register, setRegister] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); 
    setBusy(true); setError('');
    const form = new FormData(event.currentTarget);
    try { 
      await refreshCsrf(); 
      await send(`/account/${register ? 'register' : 'login'}`, 
        { email: form.get('email'), password: form.get('password') }); 
        onComplete(); 
      }catch (e) { 
        setError((e as Error).message);
      } finally { setBusy(false); }
  };

  return <main className="auth-wrap">
      <div className="auth-story">
        <div className="brand"><span className="brand-icon">
          ♞</span>Chess<span>Lens</span>
        </div>
        <p className="eyebrow">YOUR NEXT BETTER MOVE</p>
        <h1>Every game has <br />something to teach you.</h1>
        <p>Bring your games. Revisit your decisions.<br />Build a practice that starts with you.</p>
        <div className="auth-line">
          <span>01 / Import</span><span>02 / Review</span><span>03 / Practise</span>
        </div>
      </div>

      <Panel className="auth-card">
        <h2>{register ? 'Start your chess journal' : 'Welcome back'}</h2>
        <p>{register ? 'Create a private workspace for your games.' : 'Sign in to pick up where you left off.'}</p>

        <form onSubmit={submit}>
          <label>Email<input name="email" type="email" autoComplete="email" required maxLength={254} /></label>
          <label>Password<input name="password" type="password" autoComplete={register ? 'new-password' : 'current-password'} required minLength={register ? 12 : 1} maxLength={128} /></label>
          {register && <small>At least 12 characters, with uppercase, lowercase, a number and a symbol.</small>}
          <ErrorNotice message={error} />
          <button className="primary" disabled={busy}>{busy ? 'Opening workspace…' : register ? 'Create account' : 'Sign in'}</button>
        </form>

        <button className="text-button" onClick={() => { 
          setRegister(!register); setError(''); 
          }}>{register ? 'Already have an account? Sign in' : 'New here? Create an account'}</button>

        <button disabled={busy} onClick={() => { 
          setBusy(true); setError(''); 
          void send('/account/demo').then(() => { 
            location.hash = '/import'; onComplete(); 
            }).catch(e => setError(e.message)).finally(() => setBusy(false)); }}>Explore an isolated demo</button>

        <small>Original sample games in a separate, temporary workspace.</small>
      </Panel>
    </main>;
}

function Dashboard() {
  const { data, error, reload } = useRemote<Page<GameSummary>>('/games');
  const { data: insights } = useRemote<InsightSnapshot | null>('/insights');
  const { data: practice } = useRemote<{ items: { id: string; state: string; reviewDueAt: string | null }[] }>('/puzzles?page=1');

  const nextPuzzle = practice?.items.find(p => 
    p.state === 'ready' && p.reviewDueAt && new Date(p.reviewDueAt).getTime() <= Date.now());

  const finding = insights?.result?.groups[0]?.summary.findings[0];

  return <>
    <div className="page-heading">
      <div>
        <p className="eyebrow">YOUR CHESS JOURNAL</p>
        <h1>A little reflection.<br /><em>A better next game.</em></h1>
      </div>
      <a className="button primary" href="#/import">＋ Import games</a>
    </div>
    <ErrorNotice message={error} retry={reload} />

    {nextPuzzle && <Panel className="practice-note">
      <p className="eyebrow">PRACTISE TODAY</p>
      <h2>A decision is ready to revisit.</h2>
      <p>Try a checked tactic from your own games, then compare your continuation.</p>
      <a className="button primary" href={`#/puzzles/${nextPuzzle.id}`}>Practise your next puzzle</a>
      </Panel>
    }

    {finding && <Panel className="practice-note">
      <p className="eyebrow">{finding.status === 'recurring-pattern' ? 'YOUR PRACTICE FOCUS' : 'AN EARLY SIGNAL TO REVIEW'}</p>
      <h2>{finding.claim}</h2>
      <p>{finding.practice}</p>
      <a href="#/weaknesses">Inspect the evidence and practice advice</a>
      <small> · Saved {new Date(insights!.createdAt).toLocaleDateString()}</small>
      </Panel>
    }

    <div className="dashboard-grid">
      <Panel className="next-step">
        <span className="pill">YOUR NEXT STEP</span>
        <h2>{data?.total ? 'Revisit a decision.' : 'Start with your own games.'}</h2>
        <p>{data?.total ? 'Open a game, replay the moves and explore what you could have played.' : 'Import a PGN to start your library. Choose your player so future insights reflect your decisions.'}</p>
        <a className="button primary" href={data?.total ? `#/games/${data.items[0].id}` : '#/import'}>{data?.total ? 'Review latest game' : 'Import your first games'} <span aria-hidden="true">↗</span></a>
      </Panel>
      <Panel className="library-stat">
        <p className="eyebrow">YOUR LIBRARY</p>
        <strong className="big-number">{data?.total ?? '—'}</strong>
        <h3>games to learn from</h3>
        <p>Each position is a chance to see the board a little differently.</p>
        <a href="#/games">Open game library →</a>
      </Panel>
    </div>
    <div className="section-title"><h2>Recent games</h2><a href="#/games">View all games →</a></div>
    {!data && !error ? <Panel aria-busy="true">Loading your games…</Panel> : 
      data?.total ? <GameList games={data.items.slice(0, 5)} /> : 
      <Panel className="empty">
        <span className="empty-piece" aria-hidden="true">♙</span>
        <h3>Your story starts here.</h3>
        <p>Your imported games will appear here, ready to review.</p>
      </Panel>
    }
    </>;
}

function GameList({ games }: { games: GameSummary[] }) {
  return <div className="game-list">
    {games.map(g => 
      <a className="game-row" href={`#/games/${g.id}`} key={g.id}>
        <span className="game-piece" aria-hidden="true">{g.playerColour === 'black' ? '♟' : '♙'}</span>
        <div className="players">
          <strong>{g.white} <span>vs</span> {g.black}</strong>
          <small>{g.opening ?? 'Opening unknown'} · {g.timeControl ?? 'Time control unavailable'}</small>
        </div>
        <span className="result">{g.result}</span>
        <div className="game-meta">
          <span>{date(g.playedAt)}</span>
          <small>{g.playerColour ? `You played ${g.playerColour}` : 'Select your player'}</small>
        </div>
        <span aria-hidden="true">↗</span>
      </a>)
    }
    </div>;
}

function Library() {
  const [page, setPage] = useState(1);
  const [colour, setColour] = useState('');
  const { data, error, reload } = useRemote<Page<GameSummary>>(`/games?page=${page}${colour ? `&colour=${colour}` : ''}`);

  return <>
    <div className="page-heading compact">
      <div>
        <p className="eyebrow">THE RAW MATERIAL</p>
        <h1>Your games</h1>
        <p>A record of your decisions. A starting point for better ones.</p>
      </div>
      <a className="button primary" href="#/import">＋ Import games</a>
    </div>
    <div className="toolbar">
      <label>Playing as<select value={colour} onChange={e => { setColour(e.target.value); setPage(1); }}>
        <option value="">Either colour</option><option value="white">White</option>
        <option value="black">Black</option></select>
      </label>
      <span>{data ? `${data.total} games` : 'Loading…'}</span>
    </div>
    <ErrorNotice message={error} retry={reload} />

    {data?.items.length ? <GameList games={data.items} /> : 
      data && <Panel className="empty">
        <h2>No games here yet.</h2>
        <p>Import a PGN or change your filter.</p>
        <a href="#/import">Import games →</a>
        </Panel>}

    {data && data.total > 20 && 
      <div className="pagination">
        <button disabled={page === 1} onClick={() => 
          setPage(page - 1)}>Previous</button>
        <span>Page {page} of {Math.ceil(data.total / 20)}</span>
        <button disabled={page * 20 >= data.total} onClick={() => setPage(page + 1)}>Next</button>
      </div>
    }
  </>;
}

function Import() {
  const [pgn, setPgn] = useState(''); 
  const [busy, setBusy] = useState(false); 
  const [error, setError] = useState('');
  const { data: jobs, error: jobsError, reload } = useRemote<Job[]>('/jobs', 2500);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); 
    setBusy(true); setError(''); 
    const form = new FormData(event.currentTarget);
    try { 
      await send<Job>('/imports/pgn', { pgn, 
        playerName: form.get('playerName') || null, maxGames: Number(form.get('maxGames')) }); 
        reload(); 
      } catch (e) { setError((e as Error).message); 
      } finally { setBusy(false); }
  };

  const file = async (f?: File) => { 
    if (!f) return; 
    if (f.size > 5 * 1024 * 1024) { 
      setError('Choose a PGN file smaller than 5 MB.'); 
      return; 
    } 
    setPgn(await f.text()); 
    setError(''); 
    };

  return <>
    <div className="page-heading compact">
      <div>
        <p className="eyebrow">BRING YOUR OWN POSITIONS</p>
        <h1>Import your games</h1>
        <p>Completed games, ready for a closer look.</p>
      </div>
    </div>
    <div className="import-grid">
      <Panel>
        <h2>Upload a PGN</h2>
        <p>Single games or a collection. Up to 5 MB per import.</p>
        <form onSubmit={submit}>
          <label className="file-drop">
            <span aria-hidden="true">⇧</span>
            <strong>Choose a PGN file</strong>
            <input type="file" accept=".pgn,text/plain,application/x-chess-pgn" onChange={e => 
              void file(e.target.files?.[0])} /></label>
          <label>
            Or paste PGN<textarea value={pgn} onChange={e => 
              setPgn(e.target.value)} rows={7} required placeholder={'[White "Your username"]\n[Black "Opponent"]\n\n1. e4 e5 …'} spellCheck={false} /></label>
          <div className="form-grid">
            <label>Your player name
              <input name="playerName" maxLength={200} placeholder="As written in the PGN" /></label>
            <label>Maximum games
              <input name="maxGames" type="number" min={1} max={200} defaultValue={50} required /></label>
          </div>
          <small>If a name is missing or ambiguous, select your colour in each game before personal insights are calculated.</small>
          <ErrorNotice message={error} />
          <button className="primary" disabled={busy || !pgn.trim()}>{busy ? 'Queuing import…' : 'Import games'}</button>
        </form>
      </Panel>
      <div>
        <Panel className="quiet-panel">
          <p className="eyebrow">A GOOD PLACE TO BEGIN</p>
          <h2>Try a recent batch.</h2>
          <p>Start with around 50 games from the same time control. Comparable games make patterns easier to interpret.</p>
          <hr />
          <p>Games are validated individually. Valid games are saved even when another record cannot be imported.</p>
          <p>Importing the same game again will keep one copy in your library.</p>
        </Panel>
      </div>
    </div>
    <div className="section-title">
      <h2>Import activity</h2>
      <a href="#/games">Open library →</a>
    </div>
    <ErrorNotice message={jobsError} retry={reload} />
    {!jobs?.length && <Panel><p>No imports yet. Your progress will appear here.</p></Panel>}
    {jobs?.map(job => 
      <Panel key={job.id} className="job">
        <div className="section-title">
          <h3>PGN import <span className="pill">{job.state}</span></h3>
          {['queued', 'running'].includes(job.state) && <button disabled={job.cancelRequested} onClick={() => void send(`/jobs/${job.id}/cancel`).then(reload).catch(e => setError(e.message))}>{job.cancelRequested ? 'Cancelling…' : 'Cancel'}</button>}
        </div>
        <progress max={job.total || 1} value={job.processed} aria-label="Import progress" />
        <p>{job.imported} imported · {job.duplicates} already in library · {job.failures.length} could not be imported</p>
        {job.error && <p role="status">{job.error}</p>}
        {job.failures.length > 0 && <details>
            <summary>View per-game issues</summary>
            <ul>{job.failures.map(f => 
              <li key={f.gameNumber}>Game {f.gameNumber}: {f.message}</li>)}</ul>
          </details>}
      </Panel>
      )
    }
  </>;
}

function Settings({ user, onUpdate, onSignOut }: { user: User; onUpdate: (u: User) => void; onSignOut: () => void }) {
  const [error, setError] = useState(''); 
  const [saved, setSaved] = useState(false); 
  const [confirm, setConfirm] = useState(false);
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); 
    const f = new FormData(event.currentTarget); 
    setError(''); 
    setSaved(false);
    try { 
      onUpdate(await send<User>('/account/preferences', Object.fromEntries(f), 'PUT'));
      setSaved(true);
     } catch (e) { 
      setError((e as Error).message); 
    }
  };
  return <>
    <div className="page-heading compact">
      <div>
        <p className="eyebrow">MAKE IT YOURS</p>
        <h1>Settings</h1>
      </div>
    </div>
    <div className="settings-grid">
      <Panel>
        <h2>Your preferences</h2>
        <form onSubmit={submit}>
          <label>Coaching level<select name="coachingLevel" defaultValue={user.coachingLevel}>
            <option value="beginner">Beginner · threats and material</option>
            <option value="intermediate">Intermediate · patterns and plans</option>
            <option value="advanced">Advanced · deeper variations</option></select></label>
            <label>Timezone
              <input name="timezone" defaultValue={user.timezone} list="timezones" required />
              <datalist id="timezones">{['UTC', Intl.DateTimeFormat().resolvedOptions().timeZone, 'Africa/Johannesburg', 'Europe/London', 'America/New_York'].map((z, i) => 
                <option key={i} value={z} />)}</datalist></label>
            <label>Preferred time control
              <select name="preferredTimeControls" defaultValue={user.preferredTimeControls}>{['bullet', 'blitz', 'rapid', 'classical', 'correspondence'].map(t => 
                <option key={t}>{t}</option>)}</select></label>
            <ErrorNotice message={error} />
            <button className="primary">Save preferences</button>
            {saved && <p role="status">Preferences saved.</p>}
        </form>
      </Panel>
      <div>
        <Panel>
          <h2>Your account</h2>
          <p>{user.email}</p>
          <button onClick={() => void send('/account/logout').then(onSignOut).catch(e => setError(e.message))}>Sign out</button>
        </Panel>
        <Panel className="danger-zone">
          <h2>Delete imported data</h2>
          <p>Remove your imported games and import history from this workspace.</p>
          {confirm ? <>
            <p>Confirm deletion? This cannot be undone.</p>
            <button className="danger" onClick={() => void send('/account/data', undefined, 'DELETE').then(() => { setConfirm(false); location.hash = '/'; }).catch(e => setError(e.message))}>Delete my imported data</button>
            <button onClick={() => setConfirm(false)}>Keep my data</button></> : <button onClick={() => setConfirm(true)}>Delete data…</button>}
        </Panel>
      </div>
    </div>
  </>;
}
