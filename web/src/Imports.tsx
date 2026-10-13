import { useState, type FormEvent } from 'react';
import { send, type Job, type ProviderRequest } from './api';
import { useRemote } from './hooks';
import { useSavedJob } from './jobHooks';
import { ErrorNotice, Panel } from './App';

type Profile = { provider: 'chesscom' | 'lichess'; username: string; ownershipVerified: boolean };
type Batch = { id: string; importJobId: string; profile: string; state: string; version: number; cancelRequested: boolean; reusedReviews: number;
  totalGames: number; completedGames: number; failedGames: number; completedPlies: number; totalPlies: number;
  runs: { id: string; gameId: string; state: string; completedPlies: number; totalPlies: number; error: string | null }[] };
const providerName = (source: string) => source === 'chesscom' ? 'Chess.com' : source === 'lichess' ? 'Lichess' : 'PGN';
const day = (date: Date) => date.toISOString().slice(0, 10);

export function ImportPage() {
  const [source, setSource] = useState('pgn'); const [username, setUsername] = useState(''); const [pgn, setPgn] = useState('');
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  const { data: jobs, error: jobsError, reload } = useRemote<Job[]>('/jobs', 2500);
  const { data: profiles, reload: reloadProfiles } = useRemote<Profile[]>('/imports/profiles');
  const queue = async (request: ProviderRequest) => {
    setBusy(true); setError('');
    try { await send<Job>('/imports/provider', request); reload(); reloadProfiles(); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  };
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget);
    if (source !== 'pgn') {
      const to = new Date(`${form.get('to')}T00:00:00Z`); to.setUTCDate(to.getUTCDate() + 1);
      await queue({ provider: source as ProviderRequest['provider'], username, from: `${form.get('from')}T00:00:00Z`, to: to.toISOString(), maxGames: Number(form.get('maxGames')) });
      return;
    }
    setBusy(true); setError('');
    try { await send<Job>('/imports/pgn', { pgn, playerName: form.get('playerName') || null, maxGames: Number(form.get('maxGames')) }); reload(); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  };
  const file = async (f?: File) => {
    if (!f) return;
    if (f.size > 5 * 1024 * 1024) { setError('Choose a PGN file smaller than 5 MB.'); return; }
    setPgn(await f.text()); setError('');
  };
  return <>
    <div className="page-heading compact"><div><p className="eyebrow">BRING YOUR OWN POSITIONS</p><h1>Import your games</h1><p>Completed games, ready for a closer look.</p></div></div>
    <div className="import-grid">
      <Panel>
        <label>Import from<select value={source} onChange={e => { setSource(e.target.value); setError(''); }}>
          <option value="pgn">PGN file or text</option><option value="chesscom">Chess.com public games</option><option value="lichess">Lichess public games</option>
        </select></label>
        <h2>{source === 'pgn' ? 'Upload a PGN' : `From ${providerName(source)}`}</h2>
        <form onSubmit={submit}>
          {source === 'pgn' ? <>
            <p>Single games or a collection. Up to 5 MB per import.</p>
            <label className="file-drop"><span aria-hidden="true">⇧</span><strong>Choose a PGN file</strong>
              <input type="file" accept=".pgn,text/plain,application/x-chess-pgn" onChange={e => void file(e.target.files?.[0])} /></label>
            <label>Or paste PGN<textarea value={pgn} onChange={e => setPgn(e.target.value)} rows={7} required placeholder={'[White "Your username"]\n[Black "Opponent"]\n\n1. e4 e5 …'} spellCheck={false} /></label>
            <label>Your player name<input name="playerName" maxLength={200} placeholder="As written in the PGN" /></label>
            <small>If a name is missing or ambiguous, select your colour in each game before personal insights are calculated.</small>
          </> : <>
            <p>Only public, completed standard games. No chess account password is needed.</p>
            <label>Public username<input value={username} onChange={e => setUsername(e.target.value)} required minLength={2} maxLength={source === 'lichess' ? 30 : 50} pattern="[a-zA-Z0-9_-]+" autoCapitalize="none" spellCheck={false} /></label>
            <div className="form-grid"><label>From date (UTC)<input name="from" type="date" defaultValue={day(new Date(Date.now() - 30 * 86400000))} required /></label>
              <label>Through date (UTC)<input name="to" type="date" defaultValue={day(new Date())} required /></label></div>
            <small>Both selected dates are included. Choose up to one year. This public profile is unverified; importing its games does not prove account ownership.</small>
          </>}
          <label>Maximum games<input name="maxGames" type="number" min={1} max={200} defaultValue={50} required /></label>
          <ErrorNotice message={error} />
          <button className="primary" disabled={busy || (source === 'pgn' ? !pgn.trim() : !username.trim())}>{busy ? 'Queuing import…' : 'Import games'}</button>
        </form>
      </Panel>
      <div><Panel className="quiet-panel"><p className="eyebrow">A GOOD PLACE TO BEGIN</p><h2>Try a recent batch.</h2>
        <p>Start with around 50 games from the same time control. Comparable games make patterns easier to interpret.</p><hr />
        <p>Valid games are saved even when another record cannot be imported. Resync keeps one copy and preserves the original source.</p>
        <p>Once imported, select up to 20 games for a quick or deeper review. You can leave this screen and return to the saved progress.</p>
        {!!profiles?.length && <><h3>Imported public profiles</h3><ul>{profiles.map(p => <li key={`${p.provider}:${p.username}`}>
          <button onClick={() => { setSource(p.provider); setUsername(p.username); }}>{providerName(p.provider)} · {p.username}</button> <small>Unverified</small></li>)}</ul></>}
      </Panel></div>
    </div>
    <div className="section-title"><h2>Import activity</h2><a href="#/games">Open library →</a></div>
    <ErrorNotice message={jobsError} retry={reload} />
    {!jobs?.length && <Panel><p>No imports yet. Your progress will appear here.</p></Panel>}
    {jobs?.map(job => <ImportActivity key={job.id} job={job} refresh={reload} busy={busy} resync={request => void queue(request)} />)}
  </>;
}

function ImportActivity({ job, refresh, busy, resync }: { job: Job; refresh: () => void; busy: boolean; resync: (request: ProviderRequest) => void }) {
  const [error, setError] = useState(''); const [review, setReview] = useState(false);
  const active = ['queued', 'running'].includes(job.state);
  return <Panel className="job">
    <div className="section-title"><h3>{providerName(job.source)} import{job.request && ` · ${job.request.username}`} <span className="pill">{job.state}</span></h3>
      {active && <button disabled={job.cancelRequested} onClick={() => void send(`/jobs/${job.id}/cancel`).then(refresh).catch(e => setError(e.message))}>{job.cancelRequested ? 'Cancelling…' : 'Cancel'}</button>}</div>
    {job.phase === 'fetching' && active ? <p role="status">Finding completed games in your selection… {job.total > 0 && `${job.total} found so far.`}</p> :
      <progress max={job.total || 1} value={job.processed} aria-label="Import progress" />}
    <p>{job.imported} imported · {job.duplicates} already in library · {job.failures.length} could not be imported</p>
    {job.error && <p role="status">{job.error}{job.nextAttemptAt && active && ` Next check: ${new Date(job.nextAttemptAt).toLocaleTimeString()}.`}</p>}
    {job.failures.length > 0 && <details><summary>View per-game issues</summary><ul>{job.failures.map(f => <li key={f.gameNumber}>Game {f.gameNumber}: {f.message}</li>)}</ul></details>}
    <ErrorNotice message={error} />
    {!active && <div className="toolbar">
      {job.imported + job.duplicates > 0 && <button className="primary" aria-expanded={review} onClick={() => setReview(!review)}>Review this batch</button>}
      {job.request && <button disabled={busy} onClick={() => resync(job.request!)}>{job.state === 'failed' ? 'Retry import' : 'Resync this selection'}</button>}
    </div>}
    {review && <BatchSelection jobId={job.id} />}
  </Panel>;
}

function BatchSelection({ jobId }: { jobId: string }) {
  const { data, error, reload } = useRemote<Batch | null>(`/jobs/${jobId}/analysis`, 5000);
  const [busy, setBusy] = useState(false); const [submitError, setSubmitError] = useState('');
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const f = new FormData(event.currentTarget); setBusy(true); setSubmitError('');
    try { const batch = await send<Batch>(`/jobs/${jobId}/analysis`, { profile: f.get('profile'), maximumGames: Number(f.get('maximumGames')) }); location.hash = `/analysis-batches/${batch.id}`; }
    catch (e) { setSubmitError((e as Error).message); } finally { setBusy(false); }
  };
  return <div><ErrorNotice message={error} retry={reload} /><ErrorNotice message={submitError} />
    {data ? <><p>{data.completedGames}/{data.totalGames} games reviewed · {data.state}</p><a className="button primary" href={`#/analysis-batches/${data.id}`}>Open saved batch review →</a></> :
      data === null && <form onSubmit={submit}><div className="form-grid"><label>Games to review<input type="number" name="maximumGames" min={1} max={20} defaultValue={10} required /></label>
        <label>Review depth<select name="profile"><option value="quick">Quick · provisional overview</option><option value="deep">Deep · more engine work</option></select></label></div>
        <p>The first selected games in this import, up to 20. Existing completed reviews are reused. At most three game reviews run or wait in the active queue; the rest start automatically. All requested moves count toward your daily budget.</p>
        <button className="primary" disabled={busy}>{busy ? 'Starting reviews…' : 'Analyse selected games'}</button></form>}
    {data === undefined && !error && <p role="status">Checking saved batch reviews…</p>}
  </div>;
}

export function AnalysisBatchPage({ id }: { id: string }) {
  const { data: batch, error, reload } = useSavedJob<Batch>(`/analysis-batches/${id}`, id, 'batch');
  const [commandError, setCommandError] = useState('');
  return <><div className="page-heading compact"><div><p className="eyebrow">YOUR NEXT STEP</p><h1>Batch review</h1><p>Saved progress. Open any finished game while the others are being reviewed.</p></div><a href="#/import">Back to imports →</a></div>
    <ErrorNotice message={error} retry={reload} /><ErrorNotice message={commandError} />
    {!batch && !error && <p role="status">Opening your saved reviews…</p>}
    {batch && <Panel><div className="section-title"><h2>{batch.completedGames}/{batch.totalGames} games reviewed</h2><span className="pill">{batch.state}</span></div>
      <progress max={batch.totalPlies || 1} value={batch.completedPlies} aria-label="Batch analysis progress" />
      <p role="status">{batch.completedPlies}/{batch.totalPlies} half-moves reviewed · {batch.profile} review{batch.reusedReviews > 0 && ` · ${batch.reusedReviews} existing reviews reused`}</p>
      {['queued', 'running'].includes(batch.state) && <button disabled={batch.cancelRequested} onClick={() => void send(`/analysis-batches/${batch.id}/cancel`).then(reload).catch(e => setCommandError(e.message))}>{batch.cancelRequested ? 'Cancelling…' : 'Cancel remaining reviews'}</button>}
      {batch.failedGames > 0 && <p>Some games could not be fully reviewed. Open a game to see the reason and retry its analysis.</p>}
      <ol>{batch.runs.map((run, i) => <li key={run.id}><a href={`#/games/${run.gameId}?run=${run.id}`}>Game {i + 1}</a> · {run.state} · {run.completedPlies}/{run.totalPlies} half-moves{run.error && <p>{run.error}</p>}</li>)}</ol>
      {batch.completedGames > 0 && <a className="button primary" href="#/weaknesses">Find supported patterns →</a>}
    </Panel>}
  </>;
}
