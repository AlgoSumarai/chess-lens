import { useEffect, useState, type FormEvent } from 'react';
import { send } from './api';
import { useRemote } from './hooks';
import { ErrorNotice, Panel } from './App';
import { CreatePuzzleButton } from './Puzzles';

type Evidence = { gameId: string; runId: string; ply: number; side: string; classification: string; source: string; timeControl: string | null; playedAt: string | null; explanation: string };
type Finding = { category: string; group: string; claim: string; status: string; confidence: string; eligibleGames: number; eligibleMoves: number; occurrences: number; affectedGames: number;
  occurrenceRate: number; lossesAmongAffectedGames: number; centipawnLossSum: number; mateTransitions: number; practice: string; evidence: Evidence[] };
type Summary = { analysedGames: number; eligibleMoves: number; verifiedErrorMoves: number; provisionalMoves: number; rejectedEvidenceMoves: number; clockMoves: number; clockCoverage: number; clockAvailable: boolean; findings: Finding[] };
type Group = { engine: string; profile: string; analysisVersion: string; summary: Summary; classificationSettings: { inaccuracyCentipawns: number; mistakeCentipawns: number; blunderCentipawns: number } };
export type InsightSnapshot = { id: string; state: string; version: number; createdAt: string; detectorVersion: string; error: string | null;
  filter: { from: string; to: string; source: string | null; colour: string | null; opening: string | null; timeControl: string | null; maximumGames: number };
  result: null | { matchingGames: number; selectedGames: number; missingIdentityGames: number; withoutCompletedAnalysis: number; undatedGamesExcluded: number; selectionCapped: boolean; groups: Group[];
    options: { minimumGames: number; recurringGames: number; minimumClockMoves: number; minimumClockCoverage: number } } };
const names: Record<string, string> = { 'hanging-material': 'Material safety', 'missed-tactics': 'Tactical opportunities', 'opening-mistakes': 'Opening decisions', 'time-trouble': 'Time use' };

export function Weaknesses() {
  const { data: snapshot, error, reload } = useRemote<InsightSnapshot | null>('/insights', 2500);
  const [pending, setPending] = useState(false); const [problem, setProblem] = useState(''); const [group, setGroup] = useState(0);
  useEffect(() => setGroup(0), [snapshot?.id]);
  const active = snapshot?.state === 'queued' || snapshot?.state === 'running';
  const result = snapshot?.result; const chosen = result?.groups[group]; const summary = chosen?.summary;
  const generate = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); setPending(true); setProblem(''); const form = new FormData(event.currentTarget);
    try { await send('/insights', { from: `${form.get('from')}T00:00:00Z`, to: `${form.get('to')}T00:00:00Z`, source: form.get('source') || null, colour: form.get('colour') || null,
      opening: form.get('opening') || null, timeControl: form.get('timeControl') || null, maximumGames: Number(form.get('maximumGames')) }); reload(); }
    catch (e) { setProblem((e as Error).message); } finally { setPending(false); }
  };
  return <><div className="page-heading compact"><div><p className="eyebrow">PATTERNS, WITH EVIDENCE</p><h1>Your weaknesses</h1><p>Find recurring decisions worth practising. A single error is an early signal, not a pattern.</p></div></div>
    <Panel><h2>Choose comparable games</h2><form onSubmit={generate}><div className="form-grid">
      <label>From (UTC)<input name="from" type="date" required defaultValue={new Date(Date.now() - 90 * 86400000).toISOString().slice(0, 10)} /></label>
      <label>To, exclusive (UTC)<input name="to" type="date" required defaultValue={new Date(Date.now() + 86400000).toISOString().slice(0, 10)} /></label>
      <label>Source<select name="source"><option value="">All sources</option><option value="pgn">PGN</option><option value="chesscom">Chess.com</option><option value="lichess">Lichess</option></select></label>
      <label>Colour<select name="colour"><option value="">Either colour</option><option value="white">White</option><option value="black">Black</option></select></label>
      <label>Opening name<input name="opening" maxLength={2048} placeholder="Exact name, or leave blank" /></label>
      <label>Exact time control<input name="timeControl" maxLength={100} placeholder="600+5, or leave blank" /></label>
      <label>Maximum recent games<input name="maximumGames" type="number" min={1} max={200} defaultValue={50} required /></label>
    </div><button className="primary" disabled={pending || active}>{pending ? 'Queuing…' : 'Find my patterns'}</button></form></Panel>
    <ErrorNotice message={error || problem || snapshot?.error || ''} retry={reload} />
    {active && <Panel><p role="status" aria-label="Insight status">{snapshot.state === 'queued' ? 'Waiting for completed game reviews…' : 'Checking patterns against your saved engine evidence…'}</p><button onClick={() => void send(`/insights/${snapshot.id}/cancel`).then(reload).catch(e => setProblem(e.message))}>Cancel insight review</button></Panel>}
    {!snapshot && !error && <Panel className="empty"><h2>Start with analysed games.</h2><p>Complete a game review and select your colour, then generate your first evidence snapshot.</p><a href="#/games">Open your games</a></Panel>}
    {snapshot && !active && !result && <Panel><p>Insight review {snapshot.state}. Adjust the selection and try again.</p></Panel>}
    {result && <><Panel className="insight-context"><p className="eyebrow">SAVED EVIDENCE SNAPSHOT</p><h2>{result.selectedGames} of {result.matchingGames} matching games selected</h2>
      <p>{snapshot!.filter.from.slice(0, 10)} to {snapshot!.filter.to.slice(0, 10)} (exclusive UTC) · {snapshot!.filter.source ?? 'All sources'} · {snapshot!.filter.colour ?? 'Either colour'} · {snapshot!.filter.timeControl ?? 'All time controls'}</p>
      <p>{result.missingIdentityGames} need player selection. {result.withoutCompletedAnalysis} have no completed review. {result.undatedGamesExcluded} undated games were excluded from date filtering.</p>
      {result.selectionCapped && <p>The selection is capped at the newest {result.selectedGames} games. Narrow the dates or raise the limit to include more.</p>}
      <small>Generated {new Date(snapshot!.createdAt).toLocaleString()} · {snapshot!.detectorVersion}. Generate a new snapshot after new reviews.</small></Panel>
      {!result.groups.length && <Panel className="empty"><h2>Not enough analysed games.</h2><p>Choose your player and complete a review for games in this date range.</p><a href="#/games">Review a game</a></Panel>}
      {result.groups.length > 1 && <label className="analysis-group">Comparable analysis group<select value={group} onChange={e => setGroup(Number(e.target.value))}>{result.groups.map((g, index) => <option key={index} value={index}>{g.engine} · {g.profile} · {g.summary.analysedGames} games · group {index + 1}</option>)}</select></label>}
      {summary && chosen && <><Panel><div className="section-title"><h2>{summary.verifiedErrorMoves} verified errors in {summary.eligibleMoves} analysed moves</h2><span className="pill">{chosen.profile}</span></div>
        <p>{summary.analysedGames} eligible games · {chosen.engine}. {summary.provisionalMoves} move results remain provisional. This is a lower-bound error count; overlapping weakness tags count only once here.</p>
        <small>Classification thresholds: {chosen.classificationSettings.inaccuracyCentipawns}/{chosen.classificationSettings.mistakeCentipawns}/{chosen.classificationSettings.blunderCentipawns} centipawns. Mate transitions are separate.</small>
        {summary.rejectedEvidenceMoves > 0 && <p>{summary.rejectedEvidenceMoves} error records lacked consistent legal evidence and were excluded.</p>}
        <p>Usable timing: {summary.clockMoves}/{summary.eligibleMoves} moves ({(summary.clockCoverage * 100).toFixed(0)}%). {!summary.clockAvailable && `Time-trouble findings are unavailable: at least ${result.options.minimumClockMoves} moves and ${(result.options.minimumClockCoverage * 100).toFixed(0)}% coverage are required.`}</p></Panel>
        {!summary.findings.length && <Panel className="empty"><h2>No supported pattern yet.</h2><p>These rules found no supported weakness. Review more comparable games; absence of a finding is not proof of error-free play.</p></Panel>}
        <div className="weakness-list">{summary.findings.map(f => <Panel key={f.category + f.group}><div className="section-title"><h2>{names[f.category]}</h2><span className="pill">{f.status === 'early-signal' ? 'Early signal' : 'Recurring pattern'}</span></div>
          <p className="eyebrow">{f.group}</p><h3>{f.claim}</h3><p>{f.occurrences}/{f.eligibleMoves} eligible moves ({(f.occurrenceRate * 100).toFixed(1)}%) · {f.confidence} confidence. {f.lossesAmongAffectedGames}/{f.affectedGames} affected games ended in losses; this is an association, not a proven cause.</p>
          {f.status === 'early-signal' && <p>A recurring pattern needs at least {result.options.minimumGames} eligible games and {result.options.recurringGames} affected games.</p>}
          <p>{f.centipawnLossSum} centipawns of measured loss across non-mate errors; {f.mateTransitions} mate transitions tracked separately.</p>
          <div className="practice-note"><strong>What to practise</strong><p>{f.practice}</p></div><details><summary>Inspect {f.evidence.length} supporting positions</summary><ul className="evidence-list">{f.evidence.map(e => <li key={e.gameId + '/' + e.ply}>
            <a href={`#/games/${e.gameId}?ply=${e.ply}&run=${e.runId}`}>Review move {Math.ceil(e.ply / 2)} · {e.side} · {e.classification}</a><p>{e.explanation}</p><small>{e.source} · {e.timeControl ?? 'Time control unavailable'} · {e.playedAt?.slice(0, 10) ?? 'Date unavailable'}</small>
            <CreatePuzzleButton runId={e.runId} ply={e.ply} />
          </li>)}</ul></details></Panel>)}</div>
      </>}
    </>}
  </>;
}
