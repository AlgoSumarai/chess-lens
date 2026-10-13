# Insight rules and evidence — v2

Insights are deterministic associations. They do not establish why a player lost, assign a human rating or reproduce another site's accuracy score. Engine doubles in tests are labelled fixtures; user-facing snapshots use the stored analysis of imported completed games.

## Selection and comparability

A snapshot covers an explicit UTC date interval, inclusive `from` and exclusive `to`, up to 366 days. Optional filters match source, player colour, opening metadata and exact time control. Unknown game dates are excluded and counted separately; import time is never substituted for the game date. The newest 50 matching games are selected by default, with a cap of 200; capped selections are disclosed. A snapshot may inspect at most 20,000 source half-moves and has a 60-second processing deadline.

Player identity must be selected. Only the latest **completed** review for each selected game is used. Missing identity and missing completed analysis are counted separately. Reviews are partitioned by engine, effective engine settings, quick/deep profile, analysis version and classification settings. Each group has its own denominators; the interface lets the user select a group rather than averaging incompatible analysis.

All complete moves by the selected player form the overall denominator, including provisional results. Only non-provisional, consistently documented errors enter the verified numerator. The verified error rate is therefore a lower-bound estimate, with provisional and rejected-evidence counts displayed. Each error occurs once in the overall numerator even when it supports several tags. Opening and timing findings use narrower, explicitly reported eligible-move denominators.

## Required evidence

An eligible error must have a meaningful classification, at least 100 centipawns loss by default, or an explicit missed/allowed forced-mate transition. Best and played search records must be complete, have an exact main line, agree with the stored scores, start with the recorded moves and replay legally from the complete source history. Inconsistent evidence is counted as rejected and cannot support an insight.

Material counts use transparent heuristic weights: pawn 100, knight/bishop 300, rook 500 and queen 900. Kings have no material-exchange value. These bookkeeping values are not a claim that Stockfish internally uses the same weights. Material changes must persist at two consecutive decision points for the original player, after opponent replies, along a legal engine continuation. A capture before an expected recapture is insufficient.

| Category | Rule | Limits |
| --- | --- | --- |
| Hanging material | The played line loses at least one pawn of material, the best line preserves more, both root scores are centipawn scores, evaluation loss is at least 75% of the material loss, and the played evaluation is no greater than the initial material balance minus half the lost material. | Conservative compensation heuristic; a favourable material count alone is insufficient. The supporting line and evaluation remain available for review. |
| Missed tactics | A verified forced mate was missed, or the best line sustains a material gain of at least one pawn that exceeds the played line's material change by at least one pawn, with meaningful evaluation loss. | Uses a generic tactical-opportunity label. It never asserts a fork, pin or skewer without motif proof. |
| Opening mistakes | Meaningful verified errors in the first ten full moves of a standard-start game, grouped by exact opening metadata (or explicitly unknown) and colour. | Custom setups are excluded. Leaving book theory is never itself an error. |
| Time trouble | A verified error ends with pre-increment remaining time no greater than 10% of initial time, clamped to 10–60 seconds. | Describes timing around the move, not causation. Supports simple `seconds` or `seconds+increment` controls only. |

## Timing coverage

PGN clock annotations are treated as post-move clocks including increment. Pre-increment remaining time is `max(0, recorded clock - increment)`. Time spent is `previous clock + increment - recorded clock`, using the same player's preceding move. Initial base time is available only for a standard starting position. A custom setup needs an earlier recorded clock for that player.

Uploaded PGN clocks come only from the selected owner's first supplied file, even when another owner shares the legal game. Public provider clocks remain immutable public source metadata. Older unattributed PGN clocks are missing until restored by that owner's reimport; they are never borrowed from a different uploader. See [annotation isolation](pgn-annotation-isolation.md). Completed insight history remains unchanged; new report v2 captures freeze the owner clock snapshot.

Absent clocks, unsupported multi-period/delay controls, non-finite values and inconsistent increases beyond increment are unavailable. A one-second rounding tolerance is allowed; it never creates negative time spent. Missing values are not zeroes.

Clock findings require at least 20 usable analysed moves and at least 50% timing coverage by default. Otherwise the interface explicitly reports time-trouble findings unavailable, even if isolated low-clock errors exist.

## Recurrence, confidence and practice

A recurring pattern needs at least five eligible games and three distinct affected games by default. Less evidence is labelled **early signal / low confidence**. Recurring findings have **moderate confidence**, a rules-based label rather than a statistical probability. No fixed number of weaknesses is generated. Repeated errors across games take priority over the cost of a single outlier.

Each finding includes occurrence and eligible counts, affected games, loss association, severity, centipawn-loss totals and a separate count of mate transitions, plus practical advice and links to every supporting move and its specific saved analysis run. Mate scores never enter centipawn totals. Evidence links select the position after the recorded error, with the original before-move best continuation in the explanation; Previous returns to the decision position.

`Insights` configuration controls `MinimumGames`, `RecurringGames`, `OpeningFullMoves`, `MinimumLoss`, `MinimumMaterial`, `MinimumClockMoves` and `MinimumClockCoverage`. Options are validated and copied into the queued snapshot. Changing current configuration or player identity cannot rewrite a completed snapshot. Generate a new snapshot to reflect new reviews or identity choices. Deleting imported data also deletes its snapshots.

## Durable generation

Snapshot requests return a job immediately. The worker uses PostgreSQL leases (three minutes), up to three attempts and idempotent terminal writes. One snapshot job may be active per owner; identical active requests reuse its ID. The quota is 20 submissions per rolling day. Cancellation and data deletion prevent an in-flight result from recreating a snapshot. No LLM or new engine search runs in the insight generator.

Personalised puzzle validation and spaced review have separate [documented rules](puzzle-rules.md). Rating-aware coaching is specified in [coaching-rules.md](coaching-rules.md). Conversion/endgame rules are documented below; reports remain subsequent work.

## Conversion and endgame rules — v2

The new detector version is `chesslens-insights-v2`; [v1 rules](analytics-rules-v1.md) remain the historical reference. Completed snapshots retain their original JSON/evidence/settings. Resumed queued v1 snapshots must execute v1 rules rather than silently adding new categories.

A **conversion opportunity** is a decision after a substantial advantage has persisted through at least two consecutive decisions by the same player (default, configurable 2–5). For centipawn opportunities, each before-move best evaluation must be at least +300 cp from that player's perspective, and the previous played evaluations must also retain at least +300 cp. All contributing records must be complete, non-provisional, legally replayable from full history and consistent with one saved engine/settings/classifier group. Missing/provisional/inconsistent records or a break in the advantage reset the sequence. The current eligible decision may be the first failure. Losing the advantage means a verified classified error whose played evaluation falls to at most +100 cp or allows a forced mate. The numerator is these failed decisions; the denominator is all verified decisions at which the persistence condition held. A later recovered advantage needs a new persistence sequence. The rule reports loss of an engine-supported advantage, not failure to win a whole game or proof of the reason for a loss.

Winning-mate opportunities use a separate persistence sequence: positive mate scores before each decision and positive mate scores after preceding played moves. Giving up that verified mating continuation is recorded as a mate-objective failure. Mate scores are never converted to centipawns, never enter cp-loss totals, and their opportunity counts are disclosed separately. Moving between cp and mate breaks a sequence instead of inventing an equivalence.

An **eligible endgame position** has no queens and at most four total non-pawn, non-king pieces on the board by default. `EndgameMaximumNonPawnPieces` is configurable 1–8; `EndgameAllowQueens` can explicitly include queen endings under that same piece-count bound. This is a transparent material-based eligibility rule, not a claim that every chess theorist uses this definition. Standard and legal custom starting positions can qualify.

The endgame denominator is the selected player's decisions in those positions with complete, non-provisional, consistent legal engine evidence. The numerator is verified classified errors losing at least 100 cp (`EndgameMinimumLoss`, configurable 50–1000) or an explicit missed/allowed forced-mate transition. Sound moves and maintained/already-facing mates are negative cases. Candidate endgame positions with absent/provisional/rejected evidence remain visible in the coverage counts; they cannot support a confident finding.

V2's total verified error numerator is the **union of unique saved run/ply errors** meeting the core significant-loss rule or one of these objective-specific rules. An error tagged conversion/endgame/material/opening/timing still counts once. Each category has its own eligible decision/game denominator. New category coverage includes candidate decisions, stable evidenced decisions, eligible opportunities, missing/provisional decisions and rejected records. Conversion cp/mate opportunities are separated. Recurrence/low-sample/confidence labels and exact saved-position links remain unchanged. Changing any new setting affects only newly requested snapshots.

The rule definitions preceded implementation. Positive, negative, sparse, boundary, mate-transition and frozen-version/settings fixtures now pass, with representative real Stockfish advantage-loss/endgame and stalemate checks. See [verification](verification.md) for dated evidence and remaining release gates.
