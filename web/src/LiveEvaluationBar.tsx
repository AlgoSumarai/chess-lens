import type { LivePositionAnalysis } from './positionAnalysis';

export function LiveEvaluationBar({ analysis, orientation }: { analysis: LivePositionAnalysis; orientation: 'white' | 'black' }) {
  const score = analysis.enabled ? analysis.whiteScore : undefined;
  const white = score ? score.kind === 'mate' ? score.value > 0 ? 100 : 0 : 50 + Math.tanh(score.value / 400) * 50 : 50;
  const label = score ? score.kind === 'mate' ? `${score.value < 0 ? '−' : ''}M${Math.abs(score.value)}`
    : `${score.value >= 0 ? '+' : ''}${(score.value / 100).toFixed(1)}` : undefined;
  const description = !analysis.enabled ? 'Position analysis is off' : analysis.pending ? 'Analysing the selected position'
    : analysis.terminal ? 'No legal moves' : analysis.error ? 'Position analysis unavailable' : label ? `White’s evaluation: ${label}` : 'No evaluation available';
  return <div className={`live-evaluation-bar ${orientation}`} role="img" aria-label={description} aria-busy={analysis.pending}>
    <div className="evaluation-white" style={{ height: `${white}%` }} />
    {analysis.enabled && <span className="live-evaluation-score">{label ?? (analysis.pending ? '…' : '—')}</span>}
  </div>;
}
