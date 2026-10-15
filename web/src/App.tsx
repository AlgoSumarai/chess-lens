import { StatusMessage } from './StatusMessage';
import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { api, refreshCsrf, send, type GameSummary, type Page, type User } from './api';
import { useRemote, useRoute } from './hooks';
import { GameReview } from './GameReview';
import { Weaknesses, type InsightSnapshot } from './Weaknesses';
import { PuzzleLibrary, PuzzlePage, PuzzleAttemptPage } from './Puzzles';
import { ImportPage, AnalysisBatchPage } from './Imports';
import { PracticeLibrary, PracticePage } from './Practice';
import { ReportsLibrary, ProgressPage, ReportPage } from './Reports';

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
  const pageName = routePath.startsWith('/games/') ? 'Game review' : routePath.startsWith('/puzzles/') ? 'Personal puzzle' : routePath.startsWith('/practice/') ? 'Mistake practice' : routePath.startsWith('/reports/') ? 'Saved report' : ({ '/': 'Overview', '/games': 'Your games', '/import': 'Import games', '/weaknesses': 'Weaknesses', '/puzzles': 'Puzzle practice', '/practice': 'Mistake practice', '/reports': 'Weekly reports', '/progress': 'Progress', '/settings': 'Settings' } as Record<string, string>)[routePath] ?? 'Your workspace';

  useEffect(() => {
    document.title = `${user ? pageName : 'Welcome'} · ChessLens`;
    if (user) document.getElementById('main')?.focus();
  }, [route, !!user]);

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
  links.splice(4, 0, ['/practice', 'Mistake practice', '↺']);
  links.splice(5, 0, ['/reports', 'Weekly reports', '▤']);
  links.splice(6, 0, ['/progress', 'Progress', '↗']);

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
      <main id="main" tabIndex={-1} aria-label={pageName}>
        { 
          route === '/' ? <Dashboard /> :
          route === '/games' ? <Library /> : 
          route === '/import' ? <ImportPage /> :
          routePath.startsWith('/analysis-batches/') ? <AnalysisBatchPage id={routePath.split('/')[2]} key={route} /> :
          routePath === '/weaknesses' ? <Weaknesses snapshotId={routeQuery.get('snapshot') ?? undefined} key={route} /> :
          route === '/practice' ? <PracticeLibrary /> :
          route === '/reports' ? <ReportsLibrary /> :
          route === '/progress' ? <ProgressPage /> :
          routePath.startsWith('/reports/') ? <ReportPage id={routePath.split('/')[2]} key={route} /> :
          routePath.startsWith('/practice/') ? <PracticePage id={routePath.split('/')[2]} attemptId={routeQuery.get('attempt') ?? undefined} key={route} /> :
          route === '/puzzles' ? <PuzzleLibrary /> : 
          routePath.startsWith('/puzzles/attempts/') ? <PuzzleAttemptPage id={routePath.split('/')[3]} key={route} /> : 
          routePath.startsWith('/puzzles/') ? <PuzzlePage id = {routePath.split('/')[2]} key = {route} /> : 
          routePath.startsWith('/games/') ? <GameReview id = {routePath.split('/')[2]} initialPly = {Number(routeQuery.get('ply') ?? 0)} runId = {routeQuery.get('run') ?? undefined} coachingLevel={user.coachingLevel} modelOptIn={user.useModelCoaching} key={route} /> :
          route === '/settings' ? <Settings user={user} onUpdate={setUser} onSignOut={() => setUser(null)} /> : 
          <Panel><h1>Page not found</h1><a href="#/">Return to overview</a></Panel>
        }
      </main>
      <footer>ChessLens · Completed games. Deliberate practice. <a href="/licenses/third-party-notices.txt" target="_blank" rel="noreferrer">Licences &amp; artwork</a></footer>
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
  const { data: sessions } = useRemote<{ items: { id: string; state: string }[] }>('/practice?page=1');
  const { data: reports } = useRemote<{ items: { id: string; state: string; period: { fromDate: string } }[] }>('/reports?kind=weekly&page=1');
  const unfinished = sessions?.items.find(session => ['preparing', 'active', 'waiting'].includes(session.state));
  const lastReport = reports?.items.find(report => report.state === 'completed');

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

    {unfinished && <Panel className="practice-note"><p className="eyebrow">PICK UP WHERE YOU LEFT OFF</p><h2>Your mistake practice is saved.</h2><p>Return to the position and work toward its objective.</p><a className="button primary" href={`#/practice/${unfinished.id}`}>Resume mistake practice</a></Panel>}

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
    <Panel className="dashboard-reflection"><h2>See what changes over time.</h2><p>Keep game evidence and training activity in view, with separate groups and clear sample sizes.</p><div className="analysis-actions"><a className="button" href={lastReport ? `#/reports/${lastReport.id}` : '#/reports'}>{lastReport ? 'Open latest weekly reflection' : 'Your weekly reports'}</a><a className="button" href="#/progress">Explore your progress</a></div></Panel>
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
      onUpdate(await send<User>('/account/preferences', { ...Object.fromEntries(f), useModelCoaching: f.get('useModelCoaching') === 'on' }, 'PUT'));
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
            <label className="checkbox-label"><input name="useModelCoaching" type="checkbox" defaultChecked={user.useModelCoaching} /> Use optional model-selected coaching when configured</label>
            <small>Factual coaching works at every level without a model key. Optional requests require a configured server budget and your selection here. Provider ratings stay separate.</small>
            <ErrorNotice message={error} />
            <button className="primary">Save preferences</button>
            {saved && <StatusMessage>Preferences saved.</StatusMessage>}
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
