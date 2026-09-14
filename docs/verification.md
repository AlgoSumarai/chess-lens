# Verification ledger

This records demonstrated behavior, not full release readiness. The complete requirements remain in [the build brief](build-brief.md).

## Current source checks — 2026-09-14

- Full backend suite: **58 passed, zero failed/skipped** in 47 seconds. Command: `dotnet test tests/ChessLens.Tests --no-restore --logger 'trx;LogFileName=backend.trx' --results-directory .local/TestResults`. Evidence: `.local/TestResults/backend.trx` (generated local artifact). Tests use PostgreSQL 18.6 and the verified official Windows Stockfish 19 binary.
- Real engine checks cover White/Black mate orientation, restricted search, repetition versus an identical board without history, the fifty-move counter, compatible cache reuse, immutable cached variations, cancellation during observed engine work and process-pool replacement.
- Durable pipeline checks cover import and analysis checkpoints, repeated delivery, exhausted leases, cancellation, and resuming a partially committed review in a new worker scope after a simulated engine interruption or host shutdown. Resumption preserves classification settings despite changed worker configuration. These are pipeline failure fixtures, not a claim of an OS-level worker crash test.
- Position-review HTTP/pipeline tests cover owner isolation, legal full source history, duplicate submission, out-of-order requests, cancellation sequence barriers, discarded late engine results and cascading data deletion. Engine outputs in these pipeline tests are explicitly labelled doubles.
- Five insight checks cover the four core categories, overlapping-tag denominators, insufficient samples, compensation, inconsistent/illegal/provisional evidence, clock coverage and increments, snapshot persistence, owner isolation, active-request deduplication, frozen historical evidence after identity changes and data deletion. Detector fixtures use labelled engine doubles. Real-engine desktop/mobile insight journeys have also passed.
- Puzzle checks include real Stockfish mating and material-winning positions with legal defensive replies; labelled doubles verify multiple acceptable mates and rejection of unstable alternatives. PostgreSQL/HTTP checks cover hidden answers, owner isolation, duplicate starts, legal validation, stale commands, hints, solved/failed attempts, review intervals, data deletion, lease recovery, cancellation and the three-attempt incomplete-evidence limit.
- The UCI batch regression reproduces a real final partial MultiPV update at an already completed depth. Complete evidence can no longer mix that update with older candidates; bounds, duplicate root moves and mixed depths are excluded. Analysis tests cover equivalent-alternative stability, shifting absolute evaluations and preserving legacy rules when resuming an older version.
- Real SignalR clients verify owner-only progress, rejection of another owner's job subscription, rejected foreign browser origins and HTTP reconciliation after reconnect.
- `npm test`: **three component regressions passed**, covering late legal-position replies, late engine-candidate replies after navigation/stop, and stale notifications/HTTP responses when changing puzzles.
- `npm run build`: passed (TypeScript and Vite; 54 modules).

## Container and browser evidence

- Locked NuGet restore and Linux `npm ci` have passed in container builds with .NET 10.0.12 and the pinned frontend dependencies.
- Local Compose startup has built and started PostgreSQL, migration, API, worker and nginx. Database migrations `Foundation`, `AnalysisJobs`, `ClassificationSettings` and `PositionReview` have applied successfully. Earlier readiness check returned HTTP 200 / ready.
- Before interactive position-review additions, desktop and Pixel 7 browser journeys both passed in **55.2 seconds total**. Each registered, imported labelled sample PGNs, selected identity, replayed/explored/flipped the board, received actual SignalR analysis frames, waited for persisted Stockfish results and selected a matching graph/notation position. No page errors or horizontal overflow were observed. Screenshots were inspected.
- Updated container build/startup passed. Readiness returned **200 / ready**. Interactive desktop/mobile journeys verified real candidates, observed position-progress frames, rapid navigation and stopping guidance. Visual inspection found mobile grid overflow missed by the original `innerWidth` assertion. After correcting grid/child sizing and panel stretch, the strengthened journeys **both passed in 41.3 seconds total** (desktop 16.9 seconds, mobile 17.1 seconds), comparing content width to `documentElement.clientWidth` with candidates visible and after stopping. Active-candidate desktop/mobile screenshots were inspected and fit their viewports. These test durations are not throughput benchmarks.

## Findings resolved during verification

- Four desktop/mobile foundation and insight journeys passed in 4.4 minutes after the v3 classification fix. Each insight journey analysed five labelled original games with real Stockfish, found the supported recurring opening error, disclosed insufficient clock sample size, and followed the exact saved-run/ply link. Desktop and mobile weakness screenshots were inspected.
- Initial puzzle browser checks passed on mobile but rejected the desktop candidate after incomplete MultiPV evidence. Instrumentation found an older complete candidate batch being modified by a late partial update, including duplicate root moves. The adapter now preserves complete batches. Restricted alternatives and defensive replies use one strongest line; incomplete validation retries within three attempts. The updated six desktop/mobile journeys **all passed in 2.3 minutes**: foundation 34.9/14.4 seconds, insights 18.9/16.4 seconds, puzzles 31.3/15.3 seconds. Puzzle journeys verified actual SignalR frames, hidden answers, persisted hints after reload, solving, playback, exact original-review links and retry. Desktop solving and mobile solved/playback screenshots were inspected. These durations are not a throughput benchmark.
- A solved attempt with multiple alternatives exposed a history-index increment inside a predicate. The corrected lookup is covered by the persisted equivalent-solution test. Independent TestServer clients now have fixture-only remote addresses so unrelated tests do not share one registration-rate bucket; production limits remain unchanged.

- An expired final lease could remain running because an empty-queue transaction rolled back its failure sweep. Import and analysis claims now commit that sweep.
- The initial 15-second Stockfish cold-start timeout was insufficient in the constrained Docker VM. Direct execution of the same bundled Linux binary completed its handshake. Startup now has a configurable 5–120 second bound and leases cover it.
- A search cutoff can leave an aspiration bound or incomplete MultiPV iteration. The adapter now selects the latest complete exact iteration consistent with Stockfish's final best move when available. Bound/incomplete results cannot enter the cache.
- Position jobs initially used an unsupported EF include after a navigation projection; the owner association now loads its game/moves before access. HTTP integration checks pass after correction.
- A 50 ms cancellation test could cancel preflight validation rather than active engine work. The process-retirement test now observes CPU work on its own engine PID before cancellation.

## Local environment

- Windows 11; native SDK 10.0.203, Node 24.15.0. Docker may need `docker desktop start` after a stopped desktop session. Tests must confirm PostgreSQL is running first; a failed run due to connection refusal is not passing evidence.
- Avast TLS interception is trusted by Windows but not the Linux build base. The optional build-only CA secret exports an already trusted public root without disabling verification or exporting private keys. That build path has passed.
- npm 12's `--package-lock-only` encountered upstream EALLOWREMOTE behavior for an optional Tailwind package. A normal isolated install regenerated the exact-version lockfile; subsequent Linux `npm ci` passed.

## Still required

All milestones remain subject to the final requirement audit. Core weakness detectors, evidence links, puzzle validation, attempts, spaced review and the training screen have passing backend/component/desktop/mobile checks. Outstanding work includes provider imports, grounded rating-aware optional model coaching, conversion/endgame detectors, mistake practice, timezone reports, comparable progress, full accessibility/security/failure-recovery checks, licence bundles, clean-checkout handover and a measured resource benchmark. No production release or public deployment is claimed.
