# HTTP and real-time contracts (implemented scope)

Requests and responses use camelCase JSON. Authentication uses an HttpOnly `chesslens.session` cookie. Before every state-changing API request, obtain `GET /api/account/csrf` and send the returned `token` as `X-CSRF-TOKEN`; refresh the token after authentication identity changes. Requests rejected by authorization return 401/403. Validation errors use Problem Details; missing or non-owned resources return 404. Rate limits return 429.

| Method and route | Contract |
| --- | --- |
| POST `/api/account/register` | `{email,password}`. Password: >=12 characters, upper/lowercase, digit and symbol. Starts a cookie session. |
| POST `/api/account/login` | `{email,password}`. Failed attempts are locked out after five failures. |
| POST `/api/account/demo` | Creates a new isolated demo owner and queues labelled sample games. |
| POST `/api/account/logout` | Ends session. |
| GET `/api/account/me` | Account ID, email, timezone, coaching level, preferred time control, demo flag. |
| PUT `/api/account/preferences` | `{timezone,coachingLevel,preferredTimeControls,useModelCoaching:false}`. |
| DELETE `/api/account/data` | Deletes the owner's associations, jobs and derived records; removes source games with no remaining owner. |
| POST `/api/imports/pgn` | `{pgn,playerName?,maxGames:50}`. Raw UTF-8 PGN <=5MB; 1–200 selected games; <=1,000 plies/game. Returns 202 with job ID and Location. |
| POST `/api/imports/provider` | `{provider:"chesscom"\|"lichess",username,from,to,maxGames:50}`. Public completed-game import; inclusive/exclusive UTC dates <=366 days, 1–200 games. Returns 202/job; identical active requests reuse it. |
| GET `/api/imports/profiles` | Owner-scoped imported public profiles and `ownershipVerified:false`. |
| GET `/api/jobs` | Latest 30 owner-scoped import jobs, including progress/version and per-game errors. |
| GET `/api/jobs/{id}` | Authoritative owner-scoped import state. |
| POST `/api/jobs/{id}/cancel` | Requests cancellation; already committed valid games remain. |
| POST `/api/jobs/{id}/analysis` | `{profile:"quick"\|"deep",maximumGames:10}`. Reserves 1–20 saved batch games under the daily half-move budget; returns 202/batch. Repeated import/profile requests reuse it. |
| GET `/api/jobs/{id}/analysis` | Latest saved batch for the owned import, or JSON `null`. |
| GET `/api/analysis-batches/{id}` | Owner-scoped counts, per-run saved states/links, reused-review count, version and cancellation state. |
| POST `/api/analysis-batches/{id}/cancel` | Cancels the new remaining reviews belonging to this batch; does not cancel reused reviews. |
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
| GET `/health/ready` | Migrated database and fresh worker/engine/lane readiness; 503 with a service category when unavailable. See [operations](operations.md). |

## SignalR

Endpoint: `/hubs/progress`. Cookie-authenticated connections join only their server-derived owner group. `SubscribeJob(id)` checks ownership and cannot grant cross-owner access. Browser origins are validated against `AllowedOrigins`.

Event `jobProgress`: `{kind:"import"|"analysis"|"position"|"insights"|"puzzle"|"batch"|"coaching",id,version,gameId?,state}`. Import state includes counts and per-game failures. Analysis state includes run counts and the latest completed move result; position state includes the exact selected FEN, sequence and candidate results when complete. Insight and puzzle notifications contain their saved state name; fetch the HTTP resource for details. Events are hints: clients fetch authoritative HTTP state on connection, reconnection and notifications, retain the greatest version, ignore stale/duplicate events and periodically reconcile if the stream is unavailable. No correctness depends on uninterrupted delivery.

Every score is `{kind:"cp"|"mate",value}` from the recorded moving player's perspective. The frontend flips the sign only for its explicitly labelled White-perspective graph. Mate distances are not converted to centipawns. Every result preserves effective engine/settings, search budget, depth, nodes, legal PV and analysis version in persistence. Engine startup has a configurable `Stockfish:StartupTimeoutSeconds` bound of 5–120 seconds (default 120 to accommodate cold container starts); analysis leases last five minutes, exceeding a startup plus the bounded verification search pair.

Current quotas: three active imports and 20 import submissions per owner/rolling day; three active mainline analysis jobs and 5,000 requested half-moves per owner/rolling day; ten minutes cumulative engine search budget per game. Retained accounting survives data deletion, and every producer shares a 1,000-pending-job ceiling. See [resource admission](resource-limits.md) for all allowances, atomic reservations and cancellation/reuse behavior. Worker defaults: at most two process slots, one engine thread per process and 64MB hash per process. A round-robin lane serves imports, mainline reviews, insights, puzzles, coaching and reports; a separate lane alternates positions and practice. Both share the same pool.

## Import identity and dates

PGN duplicate fingerprints v1 combine normalized mainline UCI moves, starting FEN, result and sorted source tags, excluding `Annotator`/`PlyCount`. Distinguishing metadata preserves separate games with the same moves. Variations/comments are preserved in raw PGN but do not alter the fingerprint. Available clocks are parsed from mainline `[%clk H:MM:SS]` comments; absent/invalid clocks remain null. Date-only values are stored at UTC midnight for date filtering; their original precision and tags remain in the immutable source. Unclosed lexical structures are resynchronized at the next line-start Event/White/Site tag and reported per game where possible.

Provider requests, caching, checkpoints, source identity and batch budgets are documented in [provider-imports.md](provider-imports.md). Coaching, practice and report contracts appear below. Insight selection, evidence, configurable thresholds, confidence labels and denominator rules are documented in [analytics-rules.md](analytics-rules.md).

## Classification settings and cache compatibility

ChessLens defaults classify a centipawn loss of at least 50/100/200 as an inaccuracy/mistake/blunder. Worker settings `Analysis:InaccuracyCentipawns`, `Analysis:MistakeCentipawns` and `Analysis:BlunderCentipawns` override these thresholds (strictly increasing, at most 10,000). Compose exposes `ANALYSIS_INACCURACY_CP`, `ANALYSIS_MISTAKE_CP` and `ANALYSIS_BLUNDER_CP` in `.env`. Mate transitions use separate rules, never a synthetic centipawn value. These are heuristics, not a proprietary accuracy formula.

At first processing, each run snapshots its classification settings. Restarting a worker with different thresholds does not reclassify or mix an existing review; a new review receives the new settings. The HTTP run view includes `classificationSettings`. Pre-snapshot partial runs retain the original 50/100/200 defaults.

Each supervised engine process has a cache bounded to 64 entries, one MiB of serialized search results and a ten-minute maximum entry age. Only complete, unbounded-score results are stored. Keys include engine identity, discovered options, effective threads/hash/skill/MultiPV, full initial FEN (including the half-move clock), complete move history, search profile/node/time limits and restricted moves. Process retirement empties its cache, including after five idle seconds or a handoff to another worker. Database-coordinated slots bound the pool to two processes across connected workers; see [engine admission and failure boundaries](resource-limits.md#engine-processes). This is an optimization; durable job recovery does not depend on the cache. Cached results preserve their original search evidence and record `cacheHit`; the run's cumulative work time does not charge the original search duration again. Returned variations are copied so consumers cannot mutate cached evidence.

## Interactive position analysis

The browser creates a random review `channelId` and sends increasing integer `sequence` values (1–1,000,000). The server records a durable sequence barrier under the owner's transaction lock. An older request receives 409; a duplicate current analysis submission returns its existing job. Sending `cancelOnly:true` with a newer sequence returns 204 and records the barrier even if the earlier analysis POST has not arrived. Another owner's channel cannot be reused.

New navigation cancels queued/running jobs in that channel. Searches already executing finish within their bounded protocol deadline, but their result cannot revive a cancelled job. The browser also checks sequence, FEN and response version before displaying candidates. Analysis is opt-in, debounced by 350 milliseconds and follows the board until stopped. HTTP polling reconciles missed `jobProgress` events with `kind:"position"`; the same owner-only SignalR rules apply.

A dedicated interactive worker lane shares the engine pool with the background lane. Each position search uses 100,000 nodes, at most two seconds of engine search and up to three candidates. UCI startup/protocol limits still apply. Jobs use five-minute leases and at most three attempts. Limits are three active review channels with work, 200 requested position jobs per rolling day (cancelled work counts), and 200 new channels per owner/rolling day. Candidate evaluations are provisional and use the selected position's side-to-move perspective; they do not create a mainline mistake classification or modify source moves.

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

## Saved decision coaching

`POST /api/coaching` accepts `{runId,ply}` for a completed, non-provisional, consistent legal saved decision owned by the account. It returns 202 and a cached explanation/job with `Location: /api/coaching/{id}`. `GET /api/coaching/{id}` returns owner-scoped state/version and the result when available; `POST /api/coaching/{id}/cancel` cancels queued/running optional work and prevents late publication. All mutations require CSRF.

The result includes evidence hash, exact run/ply/FEN, level, distinct provider/pool/time-control rating context, what happened, why it mattered, legal UCI/SAN continuation, one reusable lesson, `mode` (`factual` or `model-selected`), fallback reason, prompt/analysis/engine versions and optional returned model/provider/response provenance. No key or positive configured budget means an immediate factual result. Unsupported structured plans fall back to facts. The model selects only supported facts/lessons and a checked line; it cannot determine mistakes or add motifs. See [coaching rules and limits](coaching-rules.md).

V2 insight responses add objective-specific `categoryCoverage` and evidence `conversionObjective` fields. A queued historical v1 snapshot retains v1 rules; completed snapshots never change after a detector/settings upgrade. See [analytics rules](analytics-rules.md).


## Mistake practice

- `POST /api/practice` `{runId,ply,weaknessSnapshotId?}` starts or returns an owned session for a verified original mistake. A supplied new-session weakness must contain that exact saved run/ply. The session's source history/evidence/colour are frozen; repeated creation returns the existing session with its original link.
- `GET /api/practice?page=1` lists saved sessions, twenty per page.
- `GET /api/practice/{id}` reconciles the session and current attempt, including version, objective, legal moves, full practice history/SAN, assistance, state and prior-attempt summaries. Pending/active attempts have no grading/answer payload. A requested hint intentionally reveals a checked legal candidate.
- `POST /api/practice/{id}/moves|hint|undo|retry|cancel` `{version,attemptId,move?}` applies a serialized command. Moves use full source/practice legality; promotions require the final `q/r/b/n`. Stale/concurrent commands return 409, invalid moves 400, unknown/other-owner sessions 404 and admission limits 429. A pending decision can be undone or cancelled. Finished attempts require retry rather than undo. Retry creates a distinct attempt and preserves old results.
- `GET /api/practice/{id}/attempts/{attemptId}` returns an owned finished attempt with its original objective, best/played grading evidence, archived undone grades, practical searches, journal, source run/ply and optional weakness link. Active attempts are available only through the session view. Imported-data deletion cascades practice and jobs; admission usage survives.

Session states are `preparing`, `active`, `waiting`, `completed`, `failed`, `cancelled`; attempt outcomes are `succeeded`, `needs-practice`, `ungraded`, `retried`, `cancelled` (or `active`). A failed engine check becomes ungraded, never a wrong chess answer. SignalR `jobProgress` adds kind `practice`, session ID/version/state only; HTTP remains authoritative. `SubscribeJob` checks session ownership. See [practice rules](mistake-practice.md) for the three-decision objectives, separate practical/full-strength budgets, terminal draws, leases and quotas.

## Weekly reports and progress

- `GET /api/reports/calendar` returns the saved timezone, current local Monday and latest completed week's local dates and exact UTC boundaries.
- `POST /api/reports/weekly` `{weekFrom?,maximumGames=50}` accepts a completed local Monday; default is the latest closed week. A matching captured dataset returns its existing revision. It returns 202 with the saved report/job and `Location`, or 200 `{state:"empty",period,detail}` when no completed review activity exists. Current/open weeks and non-Mondays return 400.
- `POST /api/progress` `{fromDate,throughDate,source?,colour?,opening?,unknownOpening=false,timeControl?,timeControlGroup?,maximumGames=50}` captures a UTC played-at sample, through-date inclusive. At most 366 days and 1–200 games per period. Sources: `pgn|chesscom|lichess`; colours: `white|black`; groups: `bullet|blitz|rapid|classical|correspondence|unknown`. An exact opening and `unknownOpening` cannot both be selected. Empty progress samples remain useful saved coverage results.
- `GET /api/reports?kind=weekly|progress&page=1` lists twenty owned snapshots per page with state/version/revision and frozen interval metadata. `GET /api/reports/{id}` adds the saved result when complete.
- `POST /api/reports/{id}/cancel` cancels queued/running work. `POST /api/reports/{id}/retry` retries its original captured evidence under fresh admission; completed results remain unchanged. Missing/other-owner snapshots return 404, quotas 429, a changed deletion epoch 409, bounded capture timeout 503 with a smaller-selection action.

Each result stores selected/matching counts, W/D/L/unknown results, coverage, independent compatible groups, unique verified-error and category-specific denominators, supported/insufficient prior-period comparisons, source/pool rating observations, separate training outcomes and an evidence-based practice plan. Representative decisions link to exact `gameId/runId/ply`. `jobProgress` adds `kind:"report"`, snapshot ID/version/state; HTTP reconciles saved state after missed notifications. All mutations require CSRF and every route/subscription is owner-scoped. See [report/progress rules](reports-progress-rules.md) for calendar, sample, quota, comparison and immutable-revision semantics.

Game detail adds `annotationState:"owned"|"public"|"unattributed"`. Uploaded PGN `rawPgn` and move clocks come only from the authenticated owner's first upload. An older unattributed game returns canonical PGN/missing clocks and can recover its own annotations through deduplicated reimport; restricted preservation bytes have no endpoint. Report v2 freezes owner clock snapshots. Retrying a completed report returns it unchanged; retrying an unfinished older-rules report returns 409 with a fresh-selection action. See [annotation isolation](pgn-annotation-isolation.md).

## Production HTTPS boundary

In Production/Staging, application routes require HTTPS; the private `/health`
checks also accept internal HTTP. A plain application request returns 400 with
an HTTPS action. The API trusts address/scheme forwarding only from configured
`ReverseProxy:KnownProxies` literal addresses, one hop with matching header
counts. nginx replaces those headers and preserves Host/port on API and hub
requests. Production startup requires explicit hosts and HTTPS origins.

Production authentication/CSRF cookies use `__Host-chesslens.session` and
`__Host-chesslens.csrf`, Secure/HttpOnly/SameSite=Strict, root path and no Domain.
All prior CSRF, owner, quota and exact-origin rules continue to apply. Browser
origins are exact authorities, including a non-default port. Development and
Testing retain their loopback HTTP cookie behavior. See [deployment](deployment.md)
for file-mounted database credentials, role privileges and real local TLS proof.
