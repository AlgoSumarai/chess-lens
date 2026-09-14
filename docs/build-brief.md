ChessLens — AI Agent Build Brief

Mission

Act as a senior full-stack engineer and product designer. Build ChessLens, a working chess improvement platform with a C# backend and TypeScript frontend. It must import a player's completed games, analyse them with Stockfish, identify recurring weaknesses across games, and turn those findings into personalised practice.

The central product question is: “Why do I keep losing, and what should I practise next?”

Deliver an application that demonstrates substantial software engineering: reliable external integrations, background processing, real-time updates, chess correctness, evidence-based analytics, grounded AI explanations, and a polished interactive interface. Implement functioning features and persistent data, not just a visual prototype.

Product outcome

A player should be able to:

Create an account and select a public Chess.com or Lichess username, or upload PGN files.

Import a bounded selection of completed games and watch analysis progress.

See their most frequent and costly weaknesses, with sample sizes and supporting positions.

Open a game, replay its moves, inspect alternatives, and understand the critical decisions.

Solve engine-validated puzzles generated from their own mistakes.

Resume positions where they previously went wrong and practise against an engine.

Review a weekly report and track changes within comparable openings and time controls.

The key journey is import → analyse → understand → practise → measure. Design the application around this journey.

Technical direction

Use these architectural defaults. Inspect an existing repository before changing its conventions. Check current official documentation and choose compatible, supported dependency versions; pin them and record the choices.

Area

Default

Backend

C#, ASP.NET Core Web API

Persistence

PostgreSQL, Entity Framework Core, versioned migrations

Frontend

React, TypeScript, Vite, Tailwind CSS

Chess board

Maintained React-compatible board component with suitable licensing

Chess rules

Established rules/PGN libraries; authoritative server-side move validation

Engine

Stockfish as a supervised UCI process inside a bounded worker pool

Background work

Durable PostgreSQL-backed job scheduling; use a maintained scheduler where practical

Real-time updates

ASP.NET Core SignalR

AI

Server-side provider abstraction for an LLM, with a deterministic fallback

Local environment

Docker Compose for API, worker, frontend and database

Validation

Backend unit/integration tests, frontend component tests, Playwright for critical journeys

Use a modular monolith with a separate worker process that shares application/domain code. Organise modules around accounts, imports, games, analysis, insights, training and reports. Avoid introducing microservices, Kubernetes, or a separate Python service without a demonstrated requirement.

Provide a simple local startup command and Windows 11/WSL-friendly instructions. The application must remain useful without an LLM key: imports, Stockfish analysis, insights, puzzles and training must still work.

1. Accounts and onboarding

Implement registration, sign-in, sign-out and protected routes using an established authentication framework. Prefer secure HttpOnly session cookies for a same-origin deployment, with appropriate CSRF protection.

Store timezone, coaching level, preferred time controls and imported usernames.

Distinguish an imported public username from verified account ownership. Reading someone's public games does not prove the user owns that chess account.

Let users specify which player represents them in uploaded PGNs. If the identity is ambiguous, request selection before calculating personal insights.

Offer a clearly labelled demo account or isolated demo workspace with reproducible sample games.

Let users delete their imported data. Scope games, jobs, reports, puzzles and SignalR access to the authenticated owner.

2. Game imports

Implement three sources: Chess.com public completed-game archives, Lichess completed-game exports, and single/multi-game PGN uploads.

Chess.com's Published-Data API is read-only and documents archive retrieval, caching and rate-limit behaviour. Use its supported public endpoints and respect response headers. Official Chess.com documentation

Consult the current Lichess API specification for export formats, optional clock fields, authentication requirements and rate limits. Official Lichess API documentation

Requirements:

Accept username, date range and maximum game count. Default to a manageable recent batch, such as 50 games, with a configurable cap.

Store provider, external game ID/URL, players, ratings when available, result, termination, colour, timestamps, time control, raw PGN and normalised mainline moves.

Preserve available clock annotations and opening metadata. Represent absent values as missing, never as fabricated zeroes.

Support legal standard-chess PGNs, comments, variations, promotions, castling, en passant and custom starting FENs. Analyse the mainline initially; preserve raw annotations.

Explicitly identify unsupported variants and malformed games. Import valid games from a mixed batch and report per-game failures.

Prevent duplicate imports using provider identifiers and documented fallback fingerprints for PGNs. Preserve distinct games with identical move sequences when metadata distinguishes them.

Apply bounded requests, timeouts, retries with backoff, cancellation and resumable import state. Handle unknown usernames, empty archives and provider outages clearly.

Never ask for a Chess.com password. Introduce provider authorisation only if a feature actually requires it.

3. Stockfish analysis

Run engine work outside HTTP request handlers. Persist job states such as queued, running, completed, failed and cancelled. Jobs must survive restarts and repeated delivery without producing duplicate records.

Use UCI for Stockfish communication. Discover supported options from the installed binary; record the engine version and effective settings. Official Stockfish UCI documentation

For each analysed move, retain:

Position before the move, played move and side to move.

Best move, principal variation and candidate alternatives when requested.

Evaluation type and value, preserving mate scores separately from centipawns.

Evaluation of the played continuation and calculated loss from the moving player's perspective.

Search depth, node count, budget, completion status and analysis version.

Correctness requirements:

Normalise score perspectives explicitly. Black's mistakes must be measured correctly; board orientation must never affect stored evaluation semantics.

Compare best and played moves under comparable search settings. Reanalyse suspicious or unstable positions before assigning high-confidence classifications.

Preserve move history where repetition and draw rules matter. A FEN-only cache key is insufficient for all history-dependent positions.

Classify inaccuracies, mistakes and blunders with documented configurable heuristics. Keep mate transitions explicit and avoid arbitrary centipawn conversion of mate scores.

Clearly label these as ChessLens classifications; do not claim equivalence with another site's proprietary accuracy score.

Separate quick review and deeper review profiles. Treat preliminary results as provisional.

Bound engine threads, memory, concurrent processes and per-job work. Handle timeouts, process crashes and cancellation without leaking processes.

Cache only compatible analyses, including engine/settings/version, position and relevant history in the cache identity.

Use multiple candidate lines selectively for puzzle validation and critical-position review. MultiPV provides alternatives but consumes search resources. Stockfish analysis trade-offs

4. Recurring weakness detection

Build a transparent rules-based analytics layer first. The LLM explains findings; it does not decide which moves were mistakes or invent patterns.

Weakness

Minimum evidence

Hanging material

A legal, engine-supported sequence winning material without adequate compensation; not simply counting attackers

Missed tactics

A validated opportunity such as a fork, pin, skewer, discovered attack or mating sequence; use a generic tactical label when the motif cannot be proven

Opening mistakes

Meaningful evaluation losses in a documented early-game window, grouped by opening and colour; leaving known theory alone is not an error

Time trouble

Available clock data showing low remaining time or relevant time-use patterns around mistakes; account for increment and missing annotations

Conversion problems

Repeated loss of substantial advantages using documented evaluation and persistence thresholds

Endgame errors

Significant mistakes within a clearly defined endgame classification

Implement the first four categories as core scope. Add conversion and endgame categories after the core detectors are trustworthy.

Each insight must include an understandable claim, date range, affected games and moves, eligible sample size, occurrence rate, severity, confidence and recommended practice. A mistake may carry multiple tags; avoid double-counting it in total error rates.

Use configurable minimum sample sizes. With too little evidence, say “early signal” or “not enough analysed games.” Do not invent a list of three weaknesses when only one is supported. Clock-based insights must show clock-data coverage and remain unavailable when there is insufficient timing evidence.

Prioritise actionable, recurring errors over one spectacular outlier. Describe associations with losses, not proven causes. Every insight must link directly to its supporting positions.

5. Personalised puzzles

Extract candidate positions immediately before the user's missed opportunity or avoidable mistake.

Validate candidates with deeper analysis, checking the best move, alternatives, defensive replies and tactical payoff.

Only publish puzzles with a clear learning objective and sufficiently stable solution. Not every mistake qualifies as a puzzle.

Support multiple acceptable moves when engine validation finds equivalent solutions. Do not reject a sound alternative just because it differs from one stored principal variation.

Handle mating solutions distinctly. Continue opponent replies from validated branches or bounded engine analysis, and stop when the training objective is resolved.

Hide the original mistake, answer and evaluation while solving.

Provide progressive hints, solution playback, an explanation and a link to the original game after completion.

Save attempts, hint usage, solve time, outcome, motif and review due date.

Implement a documented spaced-review rule that resurfaces failed puzzles sooner.

Label difficulty as an estimate; do not present an uncalibrated puzzle score as an official rating.

6. Interactive analysis board

Provide move navigation, clickable notation, keyboard controls, board flipping, last-move highlights, promotion selection, legal-move indicators, an evaluation graph and an evaluation bar.

Selecting a graph point or insight must open the exact corresponding position. Allow users to explore alternative lines and return to the original mainline without modifying the imported game. Validate moves on the server as well as in the client.

Keep board position, move list, evaluation and coaching explanation synchronised. Include responsive touch controls and a readable notation alternative. Do not require drag-and-drop as the only input method.

7. Rating-aware AI coaching

Generate explanations from structured, verified evidence: position, played move, best alternatives, legal engine line, supported motif, evaluation change and selected coaching level.

Beginner explanations should focus on threats, material and short sequences.

Intermediate explanations should discuss calculation, tactical patterns and practical plans.

Advanced explanations can include deeper variations and positional trade-offs when supported.

Allow a user-selected level. Treat provider and time-control ratings separately; do not assume Chess.com and Lichess ratings are interchangeable.

Prefer concise explanations: what happened, why it mattered, a better move and one reusable lesson.

Validate generated move references and structured output. Reject unsupported claims and fall back to factual templates when necessary.

Treat PGN comments, usernames and imported text as untrusted data, never as model instructions.

Keep API keys server-side. Apply request limits, caching, timeouts and an explicit cost budget. Send only the data required for the explanation.

Record prompt/model versions and evidence references for debugging. Do not claim that text validation guarantees complete chess understanding.

8. Weekly improvement reports

Generate an in-app report for each completed local-calendar week with new analysed activity. Support on-demand generation and idempotent scheduled generation; this is an application feature, not an instruction to schedule a ChatGPT task now.

Include games analysed, wins/draws/losses, data coverage, top supported weaknesses, representative positions, training completion and a short practice plan. Compare against the preceding comparable period where sufficient data exists.

Show source, colour, time-control and sample-size context. Report “insufficient data” for unsupported comparisons. Never interpret puzzle completion alone as proof of improved match performance. Store report snapshots and analysis versions so historical reports remain interpretable after reanalysis. Email delivery is optional future scope.

9. “Play against your mistakes”

Start a practice session from a selected position immediately before a past mistake. The user controls their original colour; Stockfish plays the opponent.

Display a concrete objective such as avoid losing material, find the tactic or preserve the advantage.

Provide retry, undo, hints and post-attempt review. Hide engine guidance during an unassisted attempt.

Evaluate success over a bounded continuation using an objective-specific rule; do not require winning an entire game from every starting position.

Use full analysis strength for grading and a separately configured practical opponent strength for play. Do not claim engine settings precisely match an online human rating.

Persist session history and link it to the source mistake and weakness.

Explain whether the user resolved the original problem, with the supporting continuation.

10. Progress comparisons

Offer filters for date range, source, colour, opening and exact/grouped time control. Show result counts, mistake rates per eligible moves, recurring weaknesses, training outcomes and rating trends where available.

Display denominators beside percentages. Keep providers and time controls distinguishable. Do not average incompatible rating pools into one purported skill rating. Mark small samples and changes in analysis coverage/settings. Opening names should come from provider metadata or a documented, appropriately licensed dataset; show unknown when classification is unavailable.

11. Real-time experience

Use SignalR to stream import progress, analysis progress, newly available move results, job completion/failure and bounded analysis of positions in review or training.

“Live analysis” means live delivery of post-game analysis and training results. Do not build assistance for ongoing competitive games or overlays that suggest moves during them.

Persist authoritative job state independently of the connection. Include job IDs and sequence/version information, reconnect with backoff, fetch missed state through HTTP and ignore stale events. Authorise subscriptions on the server; knowing a job ID must not grant access. Throttle updates and cancel superseded position requests when the user navigates rapidly.

User interface

Build these connected screens: onboarding/import, dashboard, game library, game analysis, weaknesses, puzzle training, mistake practice, reports and settings.

Use a distinctive, restrained chess-focused design: dark navy or charcoal surfaces, warm neutral board squares, clear typography and a small accent palette. Prioritise the board and the next useful action. Use original or properly licensed assets.

The dashboard should make “what should I practise today?” immediately answerable. Include useful loading, partial-result, empty, error and retry states. Support mobile layouts, visible keyboard focus, sufficient contrast and reduced-motion preferences. Do not rely on colour alone for move classifications.

Data and service contracts

Model users/preferences, imported profiles, import jobs, games, user-game associations, moves, versioned analysis runs, move analyses, mistake evidence, insight snapshots, puzzles/solutions, attempts, training sessions and weekly reports.

Separate immutable source games from derived analysis. Retain provenance and versions. Use indexes for common owner/date/status/source queries, pagination, migrations and database constraints for duplicate prevention.

Document HTTP endpoints for imports, jobs, games/moves, analysis, insights, puzzles/attempts, practice sessions and reports. Long-running requests should return a job reference promptly. Use a consistent validation/error format and document SignalR event payloads. Generate or share typed frontend contracts where practical.

Reliability and operational requirements

Enforce ownership on every relevant HTTP operation and real-time subscription.

Validate file sizes, game counts, move counts and resource budgets. Prevent arbitrary URL fetching and shell command construction from user input.

Apply per-user quotas to engine and AI work so one account cannot exhaust the service.

Use durable job leasing/retries and idempotent writes. Recover interrupted jobs and clearly identify permanent failures.

Log structured errors, correlation IDs, job timings and engine failures without exposing credentials or unnecessary personal data.

Provide health/readiness checks, environment examples and deployment documentation for a host that supports long-running CPU workers and persistent storage.

Document licences and attribution for Stockfish, board pieces, rules libraries and opening data. Check applicable redistribution obligations before packaging binaries.

Do not purchase services, create paid infrastructure or publish the application without an explicit instruction to do so. Produce a reviewable local build and deployment configuration first.

Implementation milestones

Complete a working vertical slice at each milestone. All listed core features belong to the final target; milestones are delivery order, not permission to omit later functionality.

Milestone

Working result

1 — Foundation

Accounts, database, PGN import, game library and legal board replay

2 — Analysis

Durable Stockfish jobs, per-move results, SignalR progress and review screen

3 — Coaching loop

Core weakness detectors, evidence links, validated puzzles and factual coaching fallback

4 — Full imports and AI

Chess.com/Lichess imports, robust resync and grounded rating-aware explanations

5 — Improvement tools

Mistake practice, spaced review, weekly reports and comparable progress views

6 — Release preparation

Accessibility, failure recovery, automated checks, demo data and reproducible deployment documentation

Acceptance criteria

From a clean checkout, documented commands start the application and its required dependencies.

A user can import PGNs and completed games from both providers; repeating an import does not duplicate the user's games.

Missing clocks, unknown openings, unsupported variants and malformed records produce honest, useful outcomes.

Real Stockfish output produces persisted analysis; the browser receives progress and recovers state after disconnection.

Known test positions verify White/Black score orientation, mate handling, promotions, castling, en passant and relevant draw-history behaviour.

A fixture set with known recurring errors produces traceable insights with correct denominators. An insufficient sample does not produce confident claims.

Generated puzzles are validated; acceptable alternative moves are handled correctly and attempts affect review scheduling.

The board, notation, graph and explanation refer to the same selected position, including after exploring a variation.

AI explanations reference provided evidence and legal continuations. Missing credentials or provider failure triggers useful factual fallback text.

Mistake practice runs from a real imported position and grades a defined learning objective.

Weekly reports use the user's timezone, avoid duplicates and do not mix incompatible rating/time-control comparisons.

Cross-user API and SignalR access is rejected. Worker restart and repeated job execution do not corrupt results.

Critical journeys work on desktop and mobile, with keyboard-accessible controls and clear failure states.

A benchmark records hardware, engine settings, game/move counts, elapsed analysis time and peak resource use. Do not claim universal speed without measurements.

Use deterministic fixtures and engine doubles for fast pipeline tests, plus a small real-Stockfish integration suite for protocol and chess correctness. Test user-visible behaviour and meaningful failure modes; avoid tests that merely mirror implementation details. Keep demo fixtures separate from production results.

Required handover

Deliver complete source, migrations, configuration examples, Docker setup, dependency lockfiles, sample PGNs, isolated demo data, automated checks, API/event documentation and a README covering setup, architecture, analytics rules, engine budgets, AI fallback and known limitations.

Include a short demonstration script: import games, inspect a supported weakness, review the position, solve a generated puzzle, practise the mistake and open a progress report.

Begin by inspecting the repository and documenting assumptions and the implementation plan. Then build milestone 1 and continue through the remaining milestones. Resolve routine technical decisions independently. If an external dependency is unavailable, preserve its real integration contract, test with labelled fixtures and report what still requires live verification. Never present mock data as a functioning production integration or claim tests/deployment succeeded without evidence.

The finished product should help a player identify a repeated mistake, understand the evidence, practise a better decision and see whether the pattern becomes less frequent in later games.