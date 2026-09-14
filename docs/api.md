# HTTP and real-time contracts (implemented scope)

Requests and responses use camelCase JSON. Authentication uses an HttpOnly `chesslens.session` cookie. Before every state-changing API request, obtain `GET /api/account/csrf` and send the returned `token` as `X-CSRF-TOKEN`; refresh the token after authentication identity changes. Requests rejected by authorization return 401/403. Validation errors use Problem Details; missing or non-owned resources return 404. Rate limits return 429.

| Method and route | Contract |
| --- | --- |
| POST `/api/account/register` | `{email,password}`. Password: >=12 characters, upper/lowercase, digit and symbol. Starts a cookie session. |
| POST `/api/account/login` | `{email,password}`. Failed attempts are locked out after five failures. |
| POST `/api/account/demo` | Creates a new isolated demo owner and queues labelled sample games. |
| POST `/api/account/logout` | Ends session. |
| GET `/api/account/me` | Account ID, email, timezone, coaching level, preferred time control, demo flag. |
| PUT `/api/account/preferences` | `{timezone,coachingLevel,preferredTimeControls}`. |
| DELETE `/api/account/data` | Deletes the owner's associations, jobs and derived records; removes source games with no remaining owner. |
| POST `/api/imports/pgn` | `{pgn,playerName?,maxGames:50}`. Raw UTF-8 PGN <=5MB; 1–200 selected games; <=1,000 plies/game. Returns 202 with job ID and Location. |
| GET `/api/jobs` | Latest 30 owner-scoped import jobs, including progress/version and per-game errors. |
| GET `/api/jobs/{id}` | Authoritative owner-scoped import state. |
| POST `/api/jobs/{id}/cancel` | Requests cancellation; already committed valid games remain. |
| GET `/api/games` | `page`, `source`, `colour`, `opening`, `timeControl`, `from`, `to`. 20 rows/page and total denominator. Date upper bound is exclusive. |
| GET `/api/games/{id}` | Immutable source metadata/PGN/mainline and the owner's selected player colour. |
| PUT `/api/games/{id}/identity` | `{colour:"white"|"black"}`. Public profile names are never proof of ownership. |
| POST `/api/games/{id}/position` | `{ply,variation:[uci...]}`. Reconstructs full source history, validates <=100 variation plies and returns canonical FEN, side, legal UCI moves and terminal state. Never mutates source games. |
| POST `/api/games/{id}/analysis` | `{profile:"quick"|"deep"}`. Returns 202/run ID; an already active game review is reused. |
| GET `/api/games/{id}/analysis` | Latest run and all currently committed move results, or null before any run. |
| GET `/api/analysis/{id}` | Specific owner-scoped versioned run. |
| POST `/api/analysis/{id}/cancel` | Requests cancellation at a bounded search checkpoint. |
| POST `/api/games/{id}/position-analysis` | `{channelId,sequence,ply,variation:[],cancelOnly:false}`. Returns 202/job and Location. Full source history plus <=100 legal variation plies; only positions belonging to an imported completed game. |
| GET `/api/position-analysis/{id}` | Owner-scoped queued/running/completed/failed/cancelled state, sequence/version, exact FEN and provisional candidate results. |
| POST `/api/insights` | `{from,to,source?,colour?,opening?,timeControl?,maximumGames:50}`. Queues a versioned evidence snapshot; 202 and job Location. Dates use an inclusive/exclusive UTC interval of at most 366 days. |
| GET `/api/insights` | Latest owner-scoped insight snapshot/job, or null. |
| GET `/api/insights/{id}` | A specific saved snapshot including filters, coverage, analysis groups, findings and supporting move/run references. |
| POST `/api/insights/{id}/cancel` | Cancels queued/running generation; a late worker result cannot revive it. |
| GET `/health/live` | Process liveness. |
| GET `/health/ready` | Database connectivity and migration readiness. |

## SignalR

Endpoint: `/hubs/progress`. Cookie-authenticated connections join only their server-derived owner group. `SubscribeJob(id)` checks ownership and cannot grant cross-owner access. Browser origins are validated against `AllowedOrigins`.

Event `jobProgress`: `{kind:"import"|"analysis"|"position"|"insights"|"puzzle",id,version,gameId?,state}`. Import state includes counts and per-game failures. Analysis state includes run counts and the latest completed move result; position state includes the exact selected FEN, sequence and candidate results when complete. Insight and puzzle notifications contain their saved state name; fetch the HTTP resource for details. Events are hints: clients fetch authoritative HTTP state on connection, reconnection and notifications, retain the greatest version, ignore stale/duplicate events and periodically reconcile if the stream is unavailable. No correctness depends on uninterrupted delivery.

Every score is `{kind:"cp"|"mate",value}` from the recorded moving player's perspective. The frontend flips the sign only for its explicitly labelled White-perspective graph. Mate distances are not converted to centipawns. Every result preserves effective engine/settings, search budget, depth, nodes, legal PV and analysis version in persistence. Engine startup has a configurable `Stockfish:StartupTimeoutSeconds` bound of 5–120 seconds (default 120 to accommodate cold container starts); analysis leases last five minutes, exceeding a startup plus the bounded verification search pair.

Current quotas: three active imports and 20 import submissions per owner/day; three active mainline analysis jobs and 5,000 requested half-moves per owner/day; ten minutes cumulative engine search budget per game. Worker defaults: at most two process slots, one engine thread per process and 64MB hash per process. One lane processes imports/mainline reviews serially; a separate interactive lane processes one position job at a time. Both share the same bounded pool. Position-review quotas are documented below.

## Import identity and dates

PGN duplicate fingerprints v1 combine normalized mainline UCI moves, starting FEN, result and sorted source tags, excluding `Annotator`/`PlyCount`. Distinguishing metadata preserves separate games with the same moves. Variations/comments are preserved in raw PGN but do not alter the fingerprint. Available clocks are parsed from mainline `[%clk H:MM:SS]` comments; absent/invalid clocks remain null. Date-only values are stored at UTC midnight for date filtering; their original precision and tags remain in the immutable source. Unclosed lexical structures are resynchronized at the next line-start Event/White/Site tag and reported per game where possible.

Provider, mistake-practice, model-coaching and report contracts will be added with their implemented milestones. Insight selection, evidence, configurable thresholds, confidence labels and denominator rules are documented in [analytics-rules.md](analytics-rules.md). Puzzle contracts are below.

## Classification settings and cache compatibility

ChessLens defaults classify a centipawn loss of at least 50/100/200 as an inaccuracy/mistake/blunder. Worker settings `Analysis:InaccuracyCentipawns`, `Analysis:MistakeCentipawns` and `Analysis:BlunderCentipawns` override these thresholds (strictly increasing, at most 10,000). Compose exposes `ANALYSIS_INACCURACY_CP`, `ANALYSIS_MISTAKE_CP` and `ANALYSIS_BLUNDER_CP` in `.env`. Mate transitions use separate rules, never a synthetic centipawn value. These are heuristics, not a proprietary accuracy formula.

At first processing, each run snapshots its classification settings. Restarting a worker with different thresholds does not reclassify or mix an existing review; a new review receives the new settings. The HTTP run view includes `classificationSettings`. Pre-snapshot partial runs retain the original 50/100/200 defaults.

Each supervised engine process has a ten-minute cache bounded to 64 entries and one MiB of serialized search results. Only complete, unbounded-score results are stored. Keys include engine identity, discovered options, effective threads/hash/skill/MultiPV, full initial FEN (including the half-move clock), complete move history, search profile/node/time limits and restricted moves. Process replacement empties its cache. This is an optimization; durable job recovery does not depend on the cache. Cached results preserve their original search evidence and record `cacheHit`; the run's cumulative work time does not charge the original search duration again. Returned variations are copied so consumers cannot mutate cached evidence.

## Interactive position analysis

The browser creates a random review `channelId` and sends increasing integer `sequence` values (1–1,000,000). The server records a durable sequence barrier under the owner's transaction lock. An older request receives 409; a duplicate current analysis submission returns its existing job. Sending `cancelOnly:true` with a newer sequence returns 204 and records the barrier even if the earlier analysis POST has not arrived. Another owner's channel cannot be reused.

New navigation cancels queued/running jobs in that channel. Searches already executing finish within their bounded protocol deadline, but their result cannot revive a cancelled job. The browser also checks sequence, FEN and response version before displaying candidates. Analysis is opt-in, debounced by 350 milliseconds and follows the board until stopped. HTTP polling reconciles missed `jobProgress` events with `kind:"position"`; the same owner-only SignalR rules apply.

A dedicated interactive worker lane shares the global engine pool with the serial import/mainline lane. Each position search uses 100,000 nodes, at most two seconds of engine search and up to three candidates. UCI startup/protocol limits still apply. Jobs use five-minute leases and at most three attempts. Limits are three active review channels with work, 200 requested position jobs per rolling day (cancelled work counts), and 200 recently used channels per owner. Candidate evaluations are provisional and use the selected position's side-to-move perspective; they do not create a mainline mistake classification or modify source moves.

For MultiPV output cut off during an iteration, the adapter prefers the latest complete exact iteration consistent with the final best move. Incomplete or bound results remain marked accordingly and cannot enter the cache.

Complete MultiPV batches are preserved as snapshots. A later partial update at the same depth cannot replace one line in an earlier batch. Ranks must arrive as a complete ordered set with distinct root moves and a common depth; the final best move must match that snapshot.

New `chesslens-analysis-v3` reviews verify critical errors by stable classification/mate transition, centipawn loss within 75, and both absolute evaluations within 75 centipawns (or unchanged mate signs). Equivalent best alternatives may change rank. The verification decision and first/final scores are retained in `budget.verification` in the stored evidence. Resumed older review versions retain their original best-move stability rule, avoiding a mixture of algorithms in one run.

## Personal puzzles

| Method | Path | Contract |
| --- | --- | --- |
| POST | `/api/puzzles` | `{runId, ply}` validates a verified mistake; returns 202 with durable candidate ID. A duplicate owner/run/ply/version reuses the candidate. |
| GET | `/api/puzzles?page=1` | Owner library, 20 per page, ready puzzles ordered by review due date. |
| GET | `/api/puzzles/{id}` | Saved validation state, objective, due date and rejection/error reason; no answer or source error. |
| POST | `/api/puzzles/{id}/cancel` | Cancel queued/running validation; late results cannot revive it. |
| POST | `/api/puzzles/{id}/attempts` | Start or resume an active attempt. Ready puzzles only; at most 200 new attempts per rolling day. |
| GET | `/api/puzzles/attempts/{id}` | Current authoritative FEN, legal moves, history, version, hints and outcome. Completed attempts include solution playback and the original saved-review link. |
| POST | `/api/puzzles/attempts/{id}/moves` | `{version, move}` submits a legal UCI move. The server plays a validated defensive reply when needed. |
| POST | `/api/puzzles/attempts/{id}/hint` | `{version}` requests the next hint stage and records assistance. |
| POST | `/api/puzzles/attempts/{id}/reveal` | `{version}` finishes the attempt and reveals the solution. |

All operations require the authenticated owner; mutations require CSRF. Stale attempt commands return 409; illegal moves return 400 without finishing the attempt. Scheduling and attempt completion commit together. `jobProgress` with `kind:"puzzle"`, `id`, `version` and saved `state` signals validation updates. The puzzle screen reconnects with backoff, ignores stale notifications and reconciles state through HTTP. See [puzzle-rules.md](puzzle-rules.md) for exact publication, resource and spaced-review rules.
