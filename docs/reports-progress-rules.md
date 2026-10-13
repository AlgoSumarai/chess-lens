# Reports and comparable progress — implementation contract v1

This contract defines the next vertical slice. It is not a verification claim.

## Calendar and activity

A week is Monday 00:00 through the following Monday 00:00 in the user's saved timezone. The interval is inclusive/exclusive, converted separately at each boundary to UTC; a DST week need not be 168 hours. At an invalid midnight, use the first valid local minute; at an ambiguous midnight, choose the earlier UTC instant. Only completed weeks are eligible. Store the timezone identifier, both local dates and exact UTC boundaries in every revision. A later timezone change creates a different series and never relabels old reports.

“Games analysed” means distinct completed source games with a completed review whose completion timestamp lies in that interval, including an older game reviewed during the week. Use the newest completed review of each game **within that interval**. Played-at dates are separate metadata: unavailable dates stay unavailable and are counted, never replaced by import/review timestamps. Results describe the selected player's result; identities or unknown results have their own coverage counts. No report is manufactured for a week without new analysed activity.

The scheduler discovers completed weeks with new review activity, in bounded resumable owner batches. Manual generation uses the same admission/capture path. A week/evidence/settings fingerprint reuses its existing revision under an owner lock. Later newly completed reviews, identity changes or changed detector settings can create a clearly numbered new immutable revision; the old revision and interval remain available. Training at the original capture cutoff remains frozen. Requests return durable jobs promptly. Jobs use leases, bounded execution/retries, cancellation and terminal-write barriers, with per-owner/global admission limits retained in the account usage ledger.

## Frozen evidence and bounds

Capture source-game identities/metadata, selected run IDs, their engine/settings/analysis/classification versions, detector options/version, training evidence/cutoff and the original date interval. Build against those exact immutable source/run references, never “latest” again after queuing. Completed results store their own coverage, findings, representative saved run/ply links and practice plan. Reanalysis cannot replace historical evidence. Imported-data deletion removes report snapshots and active work; late workers cannot recreate them.

A report/progress snapshot selects at most 200 games and 20,000 source half-moves, with a 60-second processing deadline. Display total matching games, selected games and whether evidence selection was capped. Counts/rates derived from selected evidence are labelled accordingly; excluded/provisional/rejected/undated/missing-identity/unreviewed data remain visible. No fresh engine/model request occurs during aggregation.

## Progress filters and grouping

Progress uses an explicit played-at date interval, inclusive/exclusive, with source, colour, exact opening metadata, exact time control and grouped time-control filters. Unknown openings stay unknown. Unknown played-at dates are excluded and counted. A progress snapshot freezes the newest completed review at capture for each selected game.

For simple `baseSeconds` or `baseSeconds+incrementSeconds`, the group uses `base + 40 × increment`: bullet <180 seconds, blitz <600, rapid <1800, classical otherwise. A supported provider's correspondence pool or a PGN `days/move` control is correspondence. Multi-period/delay/malformed/missing controls are separately **unknown**; do not infer a group from a username or a rating. The exact original control is also retained and shown.

Partition comparisons by provider, rating pool (unknown kept separate), player colour, opening metadata, time-control group, engine/effective settings, analysis profile/version, classification thresholds and detector version/options. Display each partition independently. Exact time controls remain available as filters and context. Available imported ratings form dated source/pool/control series, with no cross-provider/pool average or conversion. Ratings are observations attached to imported games, not an authoritative live account rating.

## Denominators and conclusions

Reuse the versioned insight rules and unique-error union. Show error counts beside the eligible-player-decision denominator and every category's narrower denominator. Disclose provisional/rejected evidence and timing coverage. Training distinguishes solved/succeeded, failed/needs-practice, assisted, cancelled/restarted and ungraded outcomes; use puzzle/practice versions and objective context. Training completion alone does not establish improved match performance.

Compare the immediately preceding equal calendar week (weekly report) or preceding equal UTC duration (progress), with the same filters/partition/settings, only when both sides have at least five reviewed games, stable evidence covers at least 80% of eligible decisions, coverage differs by at most ten percentage points and neither sample is capped. Otherwise say **insufficient comparable data** and explain why. An observed difference in supported error rates is descriptive, not proof that training caused improvement. Changes in sample/provider/pool/settings must not be hidden in a combined percentage.

Include selected reviewed games and W/D/L/unknown counts, coverage, supported top weaknesses and exact representative positions, recorded training activity, a short evidence-based practice plan and the supported/insufficient prior-period comparison. No invented weakness or plan detail is filled in when evidence is absent. Email remains future scope.
