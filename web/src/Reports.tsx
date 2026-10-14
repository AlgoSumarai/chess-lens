import { StatusMessage } from './StatusMessage';
import { useEffect, useState, type FormEvent } from 'react';
import { send } from './api';
import { useRemote } from './hooks';
import { useSavedJob } from './jobHooks';
import { ErrorNotice, Panel } from './App';

type Period = { kind: string; timezone: string; fromDate: string; toDate: string; from: string; to: string };
type Comparison = { status: string; explanation: string; currentErrors: number | null; currentEligible: number | null; previousErrors: number | null; previousEligible: number | null; rateDifference: number | null };
type Evidence = { gameId: string; runId: string; ply: number; side: string; classification: string; explanation: string };
type Finding = { category: string; claim: string; status: string; confidence: string; occurrences: number; eligibleMoves: number; eligibleGames: number; practice: string; evidence: Evidence[] };
type Coverage = { selectedGames: number; reviewedGames: number; missingIdentityGames: number; unreviewedGames: number; undatedGames: number;
  playerDecisions: number; completeDecisions: number; stableDecisions: number; provisionalDecisions: number; rejectedDecisions: number; selectionCapped: boolean; contextGames: number; contextReviewedGames: number };
type Counts = { wins: number; draws: number; losses: number; unknown: number };
type Group = { identity: string; source: string; ratingPool: string; colour: string; opening: string; timeControlGroup: string; rated: boolean | null; exactTimeControls: string[];
  analysis: { engine: string; profile: string; analysisVersion: string; settingsKnown: boolean }; coverage: Coverage; results: Counts; comparison: Comparison;
  summary: { eligibleMoves: number; verifiedErrorMoves: number; clockMoves: number; clockCoverage: number; clockAvailable: boolean; findings: Finding[] };
  weaknessRates: { category: string; occurrences: number; eligibleDecisions: number; eligibleGames: number; comparison: Comparison }[];
  ratings: { gameId: string; playedAt: string; rating: number; timeControl: string | null; rated: boolean | null }[] };
type Training = { kind: string; version: string; objective: string; attempts: number; successfulUnassisted: number; successfulAssisted: number; needsPractice: number; cancelledOrRestarted: number; ungraded: number };
type Report = { id: string; kind: string; revision: number; state: string; version: number; createdAt: string; error: string | null; rulesVersion: string; detectorVersion: string;
  period: Period; previousPeriod: Period; capturedAt: string; maximumGames: number; filter: Record<string, unknown> | null;
  result: null | { matchingGames: number; coverage: Coverage; results: Counts; groups: Group[]; training: Training[]; trainingCount: number; trainingCapped: boolean; practicePlan: string[]; duePuzzles: string[]; undatedGamesExcluded: number } };
type Queued = Report | { state: 'empty'; period: Period; detail: string };
const names: Record<string, string> = { 'hanging-material': 'Material safety', 'missed-tactics': 'Tactical opportunities', 'opening-mistakes': 'Opening decisions', 'time-trouble': 'Time use', 'conversion-problems': 'Keeping an advantage', 'endgame-errors': 'Endgame decisions' };
const provider = (source: string) => ({ chesscom: 'Chess.com', lichess: 'Lichess', pgn: 'PGN' }[source] ?? source);
const date = (value: string) => new Date(`${value.slice(0, 10)}T12:00:00Z`).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
const lastDate = (value: string) => new Date(new Date(`${value}T00:00:00Z`).getTime() - 86400000).toISOString().slice(0, 10);
const range = (period: Period) => `${date(period.fromDate)} – ${date(lastDate(period.toDate))}`;
const rate = (count: number, denominator: number) => denominator ? `${count}/${denominator} · ${(100 * count / denominator).toFixed(1)}%` : 'No eligible decisions';
const difference = (value: number) => `${value > 0 ? '+' : ''}${(value * 100).toFixed(1)} percentage points`;

export function ReportsLibrary() {
  const [page, setPage] = useState(1); const [week, setWeek] = useState(''); const [busy, setBusy] = useState(false); const [problem, setProblem] = useState(''); const [empty, setEmpty] = useState('');
  const { data, error, reload } = useRemote<{ total: number; items: Report[] }>(`/reports?kind=weekly&page=${page}`, 5000);
  const { data: calendar } = useRemote<{ timezone: string; latestCompleted: Period; currentWeekFrom: string }>('/reports/calendar');
  useEffect(() => { if (calendar && !week) setWeek(calendar.latestCompleted.fromDate); }, [calendar, week]);
  const generate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true); setProblem(''); setEmpty(''); const form = new FormData(event.currentTarget);
    try { const queued = await send<Queued>('/reports/weekly', { weekFrom: week, maximumGames: Number(form.get('maximumGames')) });
      if ('id' in queued) location.hash = `/reports/${queued.id}`; else setEmpty(`${range(queued.period)} · ${queued.detail}`); reload(); }
    catch (e) { setProblem((e as Error).message); } finally { setBusy(false); }
  };
  return <><div className="page-heading compact"><div><p className="eyebrow">REFLECTION OVER TIME</p><h1>Your weekly reports</h1><p>Keep a saved record of what you reviewed, what the evidence supports and what to practise.</p></div><a className="button primary" href="#/progress">Explore progress</a></div>
    <Panel><h2>A completed week</h2><p>Monday through Sunday in {calendar?.timezone ?? 'your saved timezone'}. Reports count reviews completed that week; some games may have been played earlier. Automatic reports are saved after completed weeks with new review activity while the worker is running.</p>
      <form className="report-form" onSubmit={generate}><label>Week starting Monday<input type="date" value={week} max={calendar?.latestCompleted.fromDate} onChange={e => setWeek(e.target.value)} required /></label>
        <label>Maximum games per period<input name="maximumGames" type="number" min={1} max={200} defaultValue={50} required /></label><button className="primary" disabled={busy || !week}>{busy ? 'Capturing the week…' : 'Generate saved report'}</button></form>
      {calendar && <small>Latest completed week: {range(calendar.latestCompleted)}. This week’s completed reviews are available in progress now.</small>}
      {empty && <StatusMessage>{empty}</StatusMessage>}
    </Panel><ErrorNotice message={error || problem} retry={reload} />
    {data?.total === 0 && <Panel className="empty"><h2>Your first weekly reflection is ahead.</h2><p>A completed week needs at least one new completed review. Complete a review, then explore its evidence in progress or return after the week closes.</p><a className="button" href="#/progress">View your progress</a><p><a href="#/games">Review your games</a></p></Panel>}
    {!data && !error && <p aria-busy="true">Loading saved reports…</p>}
    <div className="report-list">{data?.items.map(report => <Panel key={report.id}><div className="section-title"><h2>{range(report.period)}</h2><span className="pill">Revision {report.revision}</span></div><p>{report.period.timezone} · {report.state === 'completed' ? 'Saved reflection' : report.state}</p><p>{report.error}</p><small>Evidence captured {new Date(report.capturedAt).toLocaleString()}. Original interval and results stay saved.</small><p><a className="button" href={`#/reports/${report.id}`}>Open saved report</a></p></Panel>)}</div>
    {!!data && data.total > 20 && <div className="pagination"><button disabled={page === 1} onClick={() => setPage(p => p - 1)}>Previous</button><span>Page {page} of {Math.ceil(data.total / 20)}</span><button disabled={page * 20 >= data.total} onClick={() => setPage(p => p + 1)}>Next</button></div>}</>;
}

export function ProgressPage() {
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [unknown, setUnknown] = useState(false);
  const { data } = useRemote<{ items: Report[] }>('/reports?kind=progress&page=1');
  const generate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setBusy(true); setError(''); const form = new FormData(event.currentTarget);
    try {
      const report = await send<Report>('/progress', { fromDate: form.get('fromDate'), throughDate: form.get('throughDate'), source: form.get('source') || null,
        colour: form.get('colour') || null, opening: unknown ? null : form.get('opening') || null, unknownOpening: unknown,
        timeControl: form.get('timeControl') || null, timeControlGroup: form.get('timeControlGroup') || null, maximumGames: Number(form.get('maximumGames')) });
      location.hash = `/reports/${report.id}`;
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return <><div className="page-heading compact"><div><p className="eyebrow">COMPARE LIKE WITH LIKE</p><h1>Your progress</h1><p>Explore game evidence within the same source, rating pool and review settings.</p></div><a className="button" href="#/reports">Weekly reports</a></div>
    <Panel><h2>Choose your game sample</h2><p>Game dates use stored UTC timestamps. Missing played-at dates are excluded and counted. Training activity uses when attempts finished, with the same source/colour/opening/time-control filters.</p>
      <form onSubmit={generate}><div className="form-grid">
        <label>Games played from (UTC)<input name="fromDate" type="date" defaultValue={new Date(Date.now() - 90 * 86400000).toISOString().slice(0, 10)} required /></label>
        <label>Through, inclusive (UTC)<input name="throughDate" type="date" defaultValue={new Date().toISOString().slice(0, 10)} required /></label>
        <label>Source<select name="source"><option value="">All sources, kept separate</option><option value="pgn">PGN</option><option value="chesscom">Chess.com</option><option value="lichess">Lichess</option></select></label>
        <label>Playing as<select name="colour"><option value="">Either colour, kept separate</option><option value="white">White</option><option value="black">Black</option></select></label>
        <label>Exact opening name<input name="opening" maxLength={2048} disabled={unknown} placeholder="Leave blank for all openings" /></label>
        <label className="checkbox-label"><input type="checkbox" checked={unknown} onChange={e => setUnknown(e.target.checked)} /> Only unknown openings</label>
        <label>Time-control group<select name="timeControlGroup"><option value="">All groups</option>{['bullet', 'blitz', 'rapid', 'classical', 'correspondence', 'unknown'].map(t => <option key={t} value={t}>{t}</option>)}</select></label>
        <label>Exact time control<input name="timeControl" maxLength={100} placeholder="300+0, or leave blank" /></label>
        <label>Maximum games per period<input name="maximumGames" type="number" min={1} max={200} defaultValue={50} required /></label>
      </div><button className="primary" disabled={busy}>{busy ? 'Capturing your selection…' : 'View saved progress'}</button></form>
      <details className="report-notes"><summary>How time controls and comparisons work</summary><p>Simple controls use base seconds + 40 increments: bullet below 3 minutes, blitz below 10, rapid below 30, classical otherwise. Daily/correspondence stays separate. Unsupported or missing controls remain unknown. Provider rating pools also stay separate.</p><p>Comparison needs at least five reviewed games in both periods, compatible settings and at least 80% review/identity and stable-decision coverage, with coverage differing by at most ten percentage points. Reused games and capped samples are not treated as independent comparisons.</p></details>
    </Panel><ErrorNotice message={error} />
    {!!data?.items.length && <Panel><h2>Your saved explorations</h2><ul className="attempt-list">{data.items.slice(0, 5).map(report => <li key={report.id}><a href={`#/reports/${report.id}`}>{range(report.period)} · revision {report.revision} · {report.state}</a></li>)}</ul></Panel>}</>;
}

export function ReportPage({ id }: { id: string }) {
  const { data: report, error, reload, connected } = useSavedJob<Report>(`/reports/${id}`, id, 'report');
  const [problem, setProblem] = useState(''); const [busy, setBusy] = useState(false); const [selected, setSelected] = useState(0);
  useEffect(() => setSelected(0), [report?.id]);
  const act = async (action: string) => { setBusy(true); setProblem(''); try { await send(`/reports/${id}/${action}`, action === 'retry' ? {} : undefined); reload(); } catch (e) { setProblem((e as Error).message); } finally { setBusy(false); } };
  if (!report) return <><a href="#/reports">← Reports</a><ErrorNotice message={error} retry={reload} /><p aria-busy="true">Opening the saved reflection…</p></>;
  const result = report.result; const group = result?.groups[selected]; const pending = ['queued', 'running'].includes(report.state);
  return <div className="saved-report"><a className="back-link" href={report.kind === 'weekly' ? '#/reports' : '#/progress'}>← {report.kind === 'weekly' ? 'Weekly reports' : 'Progress'}</a>
    <div className="page-heading compact"><div><p className="eyebrow">SAVED EVIDENCE · REVISION {report.revision}</p><h1>{report.kind === 'weekly' ? 'Your weekly reflection' : 'Your progress snapshot'}</h1><p>{range(report.period)} · {report.period.timezone}</p><small>{report.kind === 'weekly' ? 'Reviews completed in this local week; game dates may be earlier.' : 'Games played in this UTC interval; training completion dates are separate.'}</small></div></div>
    <ErrorNotice message={error || problem || report.error || ''} retry={reload} />
    <StatusMessage aria-label="Report status">{report.state === 'completed' ? 'Saved report ready.' : pending ? report.state === 'queued' ? 'Waiting to build your saved reflection…' : 'Checking the captured reviews and comparable samples…' : `Report ${report.state}.`}</StatusMessage>
    {pending && <Panel><p>{connected ? 'Progress updates connected.' : 'Reconnecting; your selection is saved.'} You can leave and return here.</p><button disabled={busy} onClick={() => void act('cancel')}>Cancel report</button></Panel>}
    {!pending && !result && <Panel><h2>Try this saved selection again</h2><p>Retry uses the original captured evidence. Choose fewer games in a new selection if its processing bound was reached.</p><button disabled={busy} className="primary" onClick={() => void act('retry')}>Retry saved report</button><p><a href={report.kind === 'weekly' ? '#/reports' : '#/progress'}>Choose a fresh selection</a></p></Panel>}
    {result && <><div className="report-metrics"><Panel><span className="eyebrow">GAME SAMPLE</span><strong className="metric-number">{result.coverage.selectedGames}/{result.matchingGames}</strong><p>selected / matching games</p></Panel><Panel><span className="eyebrow">REVIEW COVERAGE</span><strong className="metric-number">{result.coverage.reviewedGames}/{result.coverage.selectedGames}</strong><p>selected games with completed reviews</p></Panel><Panel><span className="eyebrow">SELECTED RESULTS</span><strong className="metric-number">{result.results.wins} / {result.results.draws} / {result.results.losses}</strong><p>wins / draws / losses · {result.results.unknown} unknown</p></Panel></div>
      <Panel><h2>What the sample covers</h2><p>{result.coverage.stableDecisions}/{result.coverage.playerDecisions} player decisions have stable evidence within the identified completed reviews. {result.coverage.provisionalDecisions} provisional · {result.coverage.rejectedDecisions} rejected evidence records.</p>
        <p>{result.coverage.missingIdentityGames} selected games need player identity · {result.coverage.unreviewedGames} need a completed review · {report.kind === 'weekly' ? `${result.coverage.undatedGames} selected games have no played-at date` : `${result.undatedGamesExcluded} undated games excluded by the date filter`}.</p>
        {result.coverage.selectionCapped && <p className="practice-note">This is a capped recent sample. Counts and rates describe the selected evidence. Reduce the selection before interpreting a comparison.</p>}
        {(result.coverage.unreviewedGames > 0 || result.coverage.missingIdentityGames > 0) && <a href="#/games">Open your games to complete the missing context</a>}
      </Panel>
      <Panel className="practice-note"><p className="eyebrow">YOUR NEXT USEFUL ACTION</p><h2>A short practice plan</h2><ul>{result.practicePlan.map(p => <li key={p}>{p}</li>)}</ul>{result.duePuzzles[0] && <a className="button primary" href={`#/puzzles/${result.duePuzzles[0]}`}>Revisit a due puzzle</a>}</Panel>
      {result.groups.length > 0 ? <><Panel><h2>Compare a consistent group</h2><p>Providers, rating pools, colours, openings and review settings have their own counts. Select the group you want to inspect.</p>
        <label>Comparison group<select value={selected} onChange={e => setSelected(Number(e.target.value))}>{result.groups.map((g, index) => <option key={g.identity} value={index}>{index + 1}. {provider(g.source)} · {g.ratingPool} · {g.colour} · {g.opening} · {g.timeControlGroup} · {g.analysis.profile} · {g.coverage.reviewedGames} games</option>)}</select></label>
      </Panel>{group && <ReportGroupView group={group} previous={report.previousPeriod} />}</> : <Panel className="empty"><h2>No identified completed-review group yet.</h2><p>Choose your player and complete a review. Missing coverage above stays visible; no weakness or trend is invented.</p><a className="button" href="#/games">Review your games</a></Panel>}
      <TrainingView rows={result.training} count={result.trainingCount} capped={result.trainingCapped} />
      <details className="report-notes"><summary>Saved interval and evidence versions</summary><p>Original interval: {report.period.from} inclusive through {report.period.to} exclusive. Timezone: {report.period.timezone}. Evidence captured {new Date(report.capturedAt).toLocaleString()}.</p><p>{report.rulesVersion} · {report.detectorVersion}. Later reviews/settings create a new snapshot; this saved result stays unchanged.</p></details>
    </>}
  </div>;
}

function ReportGroupView({ group, previous }: { group: Group; previous: Period }) {
  return <><Panel><p className="eyebrow">{provider(group.source)} · {group.ratingPool} POOL · {group.colour}</p><h2>{group.opening}</h2><p>{group.timeControlGroup} · {group.analysis.profile} reviews · {group.analysis.engine} · {group.rated === true ? 'rated' : group.rated === false ? 'unrated' : 'rating mode unknown'}</p><p>Exact controls: {group.exactTimeControls.join(', ')}. {group.coverage.contextReviewedGames}/{group.coverage.contextGames} same-context selected games have identity and completed reviews.</p>
    <h3>Verified error rate</h3><p className="metric-number" role="group" aria-label="Verified error rate">{rate(group.summary.verifiedErrorMoves, group.summary.eligibleMoves)}</p><p>Errors / complete player decisions. Overlapping weakness tags count once. Provisional or rejected decisions can hide additional errors.</p>
    <div className="comparison-block"><h3>{group.comparison.status === 'supported' ? 'An observed change' : 'Insufficient comparable data'}</h3><p>Preceding period: {range(previous)}.</p>{group.comparison.status === 'supported' && <><p>Previous: {rate(group.comparison.previousErrors!, group.comparison.previousEligible!)} · Current: {rate(group.comparison.currentErrors!, group.comparison.currentEligible!)}.</p><strong>{difference(group.comparison.rateDifference!)} observed.</strong><ErrorRateChart comparison={group.comparison} /></>}<p>{group.comparison.explanation}</p></div>
  </Panel>
    <Panel><h2>Weakness rates and eligible samples</h2><div className="table-scroll"><table className="report-table"><thead><tr><th scope="col">Category</th><th scope="col">Occurrences / eligible decisions</th><th scope="col">Preceding comparison</th></tr></thead><tbody>{group.weaknessRates.map(r => <tr key={r.category}><th scope="row">{names[r.category] ?? r.category}</th><td>{rate(r.occurrences, r.eligibleDecisions)}<small>{r.eligibleGames} eligible games</small></td><td>{r.comparison.status === 'supported' ? difference(r.comparison.rateDifference!) : 'Insufficient comparable data'}<details><summary>Comparison context</summary><p>{r.comparison.explanation}</p></details></td></tr>)}</tbody></table></div><p>{group.summary.clockMoves} usable clock decisions · {(group.summary.clockCoverage * 100).toFixed(0)}% clock coverage. {group.summary.clockAvailable ? 'Timing evidence meets the configured threshold.' : 'Time-use findings are unavailable for this sample.'}</p></Panel>
    <div className="weakness-list">{group.summary.findings.map(f => <Panel key={f.category + f.claim}><div className="section-title"><h2>{names[f.category] ?? f.category}</h2><span className="pill">{f.status === 'recurring-pattern' ? 'Recurring pattern' : 'Early signal'}</span></div><p>{f.claim}</p><p>{rate(f.occurrences, f.eligibleMoves)} eligible decisions · {f.confidence} confidence.</p><p>{f.practice}</p><h3>Representative saved decisions</h3><ul className="evidence-list">{f.evidence.map(e => <li key={e.runId + '/' + e.ply}><a href={`#/games/${e.gameId}?ply=${e.ply}&run=${e.runId}`}>Review move {Math.ceil(e.ply / 2)} · {e.side} · {e.classification}</a><p>{e.explanation}</p></li>)}</ul></Panel>)}</div>
    {group.summary.findings.length === 0 && <Panel><h2>No supported weakness in this group.</h2><p>This sample did not support a finding under the saved rules. It is not a claim that the play was perfect.</p></Panel>}
    <RatingView group={group} />
  </>;
}

function ErrorRateChart({ comparison }: { comparison: Comparison }) {
  const rows = [{ name: 'Previous', value: comparison.previousErrors! / comparison.previousEligible! }, { name: 'Current', value: comparison.currentErrors! / comparison.currentEligible! }];
  return <svg className="rate-chart" role="img" aria-label={`Verified error rates, previous ${(rows[0].value * 100).toFixed(1)} percent, current ${(rows[1].value * 100).toFixed(1)} percent. Counts are listed above.`} viewBox="0 0 420 116">
    {rows.map((r, i) => <g key={r.name}><text x="0" y={34 + i * 48}>{r.name}</text><rect x="85" y={12 + i * 48} width="260" height="28" rx="4" className="chart-track" /><rect x="85" y={12 + i * 48} width={260 * r.value} height="28" rx="4" className={i ? 'chart-current' : 'chart-previous'} /><text x="358" y={33 + i * 48}>{(r.value * 100).toFixed(1)}%</text></g>)}
  </svg>;
}

function RatingView({ group }: { group: Group }) {
  const [showAll, setShowAll] = useState(false); const rows = showAll ? group.ratings : group.ratings.slice(-10);
  const known = group.ratingPool !== 'unknown' && group.source !== 'pgn';
  const first = group.ratings[0]; const last = group.ratings.at(-1);
  return <Panel><h2>Imported rating observations</h2><p>{provider(group.source)} · {group.ratingPool} pool. These ratings were attached to imported games, not a live account lookup.</p>
    {known && group.ratings.length >= 2 && <p>Available trend: {first.rating} → {last!.rating} across {group.ratings.length} dated observations. Source and rating pool are kept separate.</p>}
    {!known && <p>A comparable rating pool was not recorded. Individual observations are shown without a combined trend.</p>}
    {rows.length ? <><div className="table-scroll"><table className="report-table"><thead><tr><th scope="col">Game date</th><th scope="col">Imported rating</th><th scope="col">Exact control</th></tr></thead><tbody>{rows.map(r => <tr key={r.gameId}><td><a href={`#/games/${r.gameId}`}>{date(r.playedAt)}</a></td><td>{r.rating}</td><td>{r.timeControl ?? 'unknown'}</td></tr>)}</tbody></table></div>{group.ratings.length > 10 && <button onClick={() => setShowAll(v => !v)}>{showAll ? 'Show latest ten observations' : `Show all ${group.ratings.length} observations`}</button>}</> : <p>No dated rating observations are available in this selected group.</p>}
  </Panel>;
}

function TrainingView({ rows, count, capped }: { rows: Training[]; count: number; capped: boolean }) {
  return <Panel><p className="eyebrow">PRACTICE IS A SEPARATE MEASURE</p><h2>Recorded training activity</h2><p>{count} attempts finished during this interval. Puzzle completion alone is not evidence of improved game performance.</p>{capped && <p>The training detail sample is capped at 2,000 attempts; the total above includes all matching finished attempts.</p>}
    {rows.length ? <div className="training-summaries">{rows.map(r => <div key={r.kind + '/' + r.version + '/' + r.objective}><h3>{r.kind === 'puzzle' ? 'Personal puzzles' : 'Mistake practice'} · {r.objective.replaceAll('-', ' ')}</h3><p>{r.successfulUnassisted} successful without in-app assistance · {r.successfulAssisted} successful with help · {r.needsPractice} to practise again.</p><p>{r.cancelledOrRestarted} cancelled/restarted/revealed/expired · {r.ungraded} ungraded · {r.attempts} recorded attempts.</p><small>{r.version}</small></div>)}</div> : <p>No finished training attempts were recorded in this interval and filter context.</p>}
    <div className="analysis-actions"><a className="button" href="#/puzzles">Personal puzzles</a><a className="button" href="#/practice">Mistake practice</a></div>
  </Panel>;
}
