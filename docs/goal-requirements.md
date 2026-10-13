Complete development of chess-lens. Act as a senior full-stack engineer and product designer. Deliver a working, polished, locally runnable application that answers: **“Why do I keep losing, and what should I practise next?”** Carry the work through implementation, verification, independent review and handover. A plan, disconnected screens or an attractive prototype does not satisfy this goal.

## 1. Establish the baseline and preserve working behavior

First inspect the current checkout, its branch/commit, any `AGENTS.md`, `README.md`, all files in `docs/`, relevant application code, database migrations, configuration, scripts and tests. The full target is `docs/build-brief.md`; use `docs/implementation-plan.md` for delivery order and `docs/verification.md` for demonstrated evidence. Translate every requirement into a traceable acceptance matrix with implementation locations, checks, evidence and status. Reconcile discrepancies against code and fresh tests rather than accepting status prose as proof.

This prompt was prepared from `main` at commit `42648a584d62482bac0651438f0f36dd617863c3`, inspected on 13 October 2026. Recheck the current state before making changes. At that baseline:

- Accounts/preferences, cookie authentication/CSRF, owner isolation, PGN imports, identity selection, demo workspaces, data deletion, legal replay and variations are implemented.
- Durable mainline and interactive Stockfish analysis, SignalR reconciliation, full-history draw handling, compatible caching, partial-review recovery, snapshotted classification settings, versioned algorithms and coherent MultiPV batches have regression coverage.
- The four core weakness detectors, evidence snapshots, engine-validated short mate/material puzzles, accepted alternatives, attempts, hints and spaced-review dates are implemented.
- Provider imports, grounded rating-aware optional AI coaching, conversion/endgame detectors, mistake practice, weekly reports, comparable progress and release preparation remain required. Coaching preferences exist, but do not establish that rating-aware coaching is implemented.
- The verification ledger reports backend, component and desktop/mobile container checks from September 2026. Some pipeline/detector checks use labelled doubles; recovery fixtures are not OS-level crash evidence, and browser durations are not throughput benchmarks. These are historical claims to revalidate, not tests run by this prompt's author. No final release gate is complete.

Keep the existing C# / ASP.NET Core / EF Core / PostgreSQL modular monolith, separate worker, supervised Stockfish pool and React / TypeScript / Vite frontend. Extend the existing modules and contracts. Make focused refactors where they improve correctness, readability or reuse; avoid an unnecessary rewrite or new service architecture. Preserve user changes and existing data, and add versioned migrations for new persistence.

Keep ChessLens limited to completed-game analysis and personal training. “Live” means delivery of post-game analysis and training results; do not add assistance for ongoing competitive games or move-suggestion overlays.

## 2. Finish the missing functionality in working vertical slices

**A. Provider imports and resync.** Implement public completed-game imports from both Chess.com and Lichess. Consult current official API documentation. Accept username, dates and a bounded game count; persist unverified imported profiles, source identifiers/URLs, ratings, colour, result, termination, timestamps, time controls, clocks and opening metadata when available. Reuse authoritative PGN/legal validation. Handle pagination or streaming, missing fields, unsupported variants and mixed failures honestly. Implement provider-compatible caching, rate-limit handling, timeouts, bounded retries/backoff, cancellation and durable resume checkpoints. Repeated imports/resync must not duplicate owner associations or overwrite immutable source games. Cover concurrent duplicate delivery and changes in provider annotations. Fetch only supported provider endpoints; usernames and imported URLs must not enable arbitrary network access. Offer a clear, bounded way to analyse the imported batch, with progress and recovery. Verify each adapter with contract fixtures and a small real public import/resync.

**B. Grounded coaching.** Implement a server-side model-provider abstraction and useful deterministic coaching at beginner, intermediate and advanced levels. The entire core application must work without a model key. Respect the selected coaching level and keep provider/time-control ratings distinct. Generate concise explanations from verified position/run/ply evidence: what happened, why it mattered, a better legal continuation and one reusable lesson. Models must not determine mistakes or invent motifs. Use a generic tactical-opportunity label unless legal, verified evidence establishes a specific motif such as a fork, pin or skewer. Validate structured output, referenced moves/lines and evidence; reject unsupported claims and fall back on factual templates for invalid responses, missing credentials, timeouts or provider failures. Treat PGN comments, usernames and imported text as untrusted data. Keep secrets server-side, minimize submitted data, bound requests/costs, cache compatible explanations and record evidence, prompt/model versions and provenance. Label factual versus model-generated coaching clearly. Test injection, illegal moves, unrelated claims, stale evidence and no-key operation. Do not present output validation as proof of complete chess understanding.

**C. Conversion and endgame insights.** Add transparent, versioned rules for repeated loss of substantial advantages and significant endgame errors. Define configurable advantage/persistence thresholds, endgame eligibility and objective-specific denominators before coding. Require stable legal engine evidence and distinguish mate transitions from centipawn losses. Preserve sample-size/confidence labels, missing-data coverage, comparability groups and exact saved-run/position links. Avoid counting overlapping tags twice in total mistake rates. Add positive, negative, sparse-data and boundary fixtures, with representative real-engine checks. Update `docs/analytics-rules.md` and freeze historical snapshots when rules/settings change.

**D. Play against your mistakes.** Add persistent practice sessions starting immediately before a real, verified mistake, with the original player controlling their colour and Stockfish playing the opponent. State a concrete objective such as protect material, find a tactic or preserve an advantage. Validate every move server-side using full source and practice history; support promotions and terminal/draw states. Provide retry, undo, hints, reload/resume and post-attempt review. Hide answers/evaluations during unassisted play and record assistance so grading distinguishes assisted from unassisted attempts. Use separate practical opponent settings and full-strength grading; do not imply those settings exactly match a human rating. Grade the learning objective over a documented bounded continuation, with evidence, rather than requiring a whole-game win or one prescribed move. Persist session/version/outcome/history and source weakness links. Engine work must use durable bounded jobs, quotas, cancellation and stale-result barriers; existing puzzle/review functionality must continue to work.

**E. Weekly reports and progress.** Generate persistent in-app reports for completed weeks with new analysed activity, on demand and through the application's durable scheduler. Define the local-calendar week explicitly and handle user timezone, daylight-saving boundaries, missing dates, empty weeks and timezone changes. Ensure concurrent/repeated generation is idempotent. Retain immutable report revisions with the original date interval, evidence, analysis/detector versions and settings so later reanalysis cannot silently rewrite history. Include games analysed, wins/draws/losses, coverage, top supported weaknesses, representative positions, training activity and a short practice plan. Compare with the preceding comparable period only when evidence supports it.

Build progress views with date/source/colour/opening/exact and grouped time-control filters, denominators beside percentages, comparable mistake/weakness rates, training outcomes and available rating trends. Define time-control grouping and analysis compatibility transparently. Keep incompatible providers, rating pools and analysis settings separate. Explain small samples and coverage changes; puzzle completion alone is not proof of better game performance. Keep unknown openings unknown unless supported by provider metadata or a documented licensed dataset. Email reports remain optional future scope.

## 3. Complete an intuitive, connected interface

Finish onboarding/import, dashboard, game library, game analysis, weaknesses, puzzles, mistake practice, reports/progress and settings as connected, functioning screens. Preserve the existing charcoal/navy surfaces, warm board and restrained accent palette. Prioritize the board, evidence and next useful action.

A new player should be able to follow **import → analyse → understand → practise → measure** without reading technical documentation. Make today's supported practice recommendation clear. Use understandable filter controls and date labels, explain provisional/early-signal results in plain language and provide direct links from findings/reports to the exact saved decision. Synchronize the board, notation, graph, selected evidence and coaching through navigation, variations, reloads and rapid position changes. Never display stale guidance for another position.

Provide actionable loading, empty, partial-result, cancellation, failure and retry states. Distinguish progress from estimates and explain recovery without exposing internal implementation details. Preserve a useful no-key experience and clearly labelled, isolated original demo data.

Verify actual desktop and mobile journeys, including small viewports, long notation/usernames, touch controls, promotion dialogs, visible candidates and 200% zoom. Check document content width against viewport width. Provide visible focus, logical keyboard navigation, click/tap and text alternatives to dragging, accessible labels/status updates, readable contrast, reduced motion and classifications that do not rely on colour alone. Use automated accessibility checks plus manual keyboard and assistive-technology inspection; document tools and limitations. Review the whole primary journey, not only screenshots of individual screens.

## 4. Finish reliability, security and release preparation

Extend ownership checks, CSRF, quotas, validation, cascading data deletion and SignalR authorization to every new entity and operation. Test multiple users, concurrent/stale commands and deletion during active work so late writes cannot recreate deleted data. Preserve immutable source games and historical evidence. Bound provider, engine and model work globally and per owner. Check worker scheduling under mixed workloads so imports/mainline reviews do not indefinitely starve insight, puzzle, practice or report jobs.

Test actual worker/process termination and restart, repeated delivery, lease exhaustion, engine failure, reconnect/missed notifications and database interruption/recovery in an isolated environment. Verify persisted state and absence of duplicate records/leaked processes, not only mocked callbacks. Add structured correlation/job/error logging without secrets, and meaningful health/readiness checks for the required services.

Deliver a clean-checkout local startup with documented Windows 11 and Linux/WSL commands, reproducible locked dependencies and migration/upgrade verification using a copy of existing data. Add reproducible automated checks in CI. Check current official dependency/security information; make justified compatible updates and refresh locks/notices rather than upgrading blindly. Resolve the documented ARM64 engine/container packaging task with verified binaries/builds and checks, alongside x86-64. If an architecture cannot be verified, record the supported matrix and keep that promised gate open.

Supply and locally validate production-oriented HTTPS, trusted-proxy, secure-cookie, origin and configuration examples, persistent keys/storage, secrets handling and backup/restore instructions. Perform a real restore into an isolated database and verify data. Bundle applicable licence texts and verify board artwork/opening-data provenance and Stockfish source/build materials for the exact distributed binary. Record release image digests. Produce a benchmark stating hardware, engine/settings, profiles, games/plies, concurrency, elapsed time, peak CPU/memory and queue/interactive responsiveness; disclose its limits.

Do not purchase services, incur model costs, provision paid infrastructure or publish/deploy externally without explicit user authorization. Prepare a reviewable local release and deployment configuration. A missing external dependency must not prevent useful independent work, but it must remain visible as an unverified acceptance gate.

## 5. Verification and completion gates

Run a fresh baseline where possible, then meaningful regression checks for each slice and the final integrated build. Keep deterministic doubles clearly labelled and include a small real-Stockfish suite. Use the repository's existing commands as a starting point:

```text
dotnet restore --locked-mode
dotnet test ChessLens.slnx
cd web
npm ci
npm test
npm run build
npx playwright install chromium
npm run test:e2e
```

Supply PostgreSQL, Stockfish and the running application as documented; run browser checks against the final Compose build as well. Record commands, dates, tested commit/environment, pass/fail/skipped counts, reports/traces/screenshots and measured results in `docs/verification.md`. Preserve the distinction between fixture, live integration, manual inspection and benchmark evidence. No unavailable tool, skipped test or fixture may be recorded as a passing live gate.

The final acceptance matrix must cover every required build-brief feature, each new vertical slice and the full connected desktop/mobile journey. Include no-key and failed-provider flows, duplicate imports, lawful move alternatives, draw history, stale requests, timezone/idempotence/comparability, security isolation, resource limits and restart recovery. Update README, API/SignalR contracts, rules, dependency/licence documentation and deployment instructions to match the finished implementation. Demonstrate: import → analyse → inspect a supported weakness → review its exact position → solve a personal puzzle → practise the mistake → open a comparable report/progress view.

## 6. Independent quality review — at most three rounds

After the integrated implementation and verification are ready, delegate review to a **separate agent that did not author the implementation**. Give it the original requirements, final commit/diff, runnable app/setup, acceptance matrix and evidence. It must independently inspect code, migrations, tests and documentation; run representative checks and use the actual desktop/mobile interface. Author assertions or passing test counts alone are insufficient.

The reviewer must score each dimension **1–10**, explain the evidence and calculate this weighted overall score:

| Dimension | Weight | Review focus |
| --- | ---: | --- |
| Code quality and maintainability | 25% | Clear boundaries/types, readable code, sound persistence, focused changes and useful tests |
| Chess/domain and feature correctness | 25% | Entire required scope, legal/history-aware behavior, sound evidence, grading and comparisons |
| Ease of use and interface quality | 25% | Intuitive complete journey, clear next actions, synchronized board/coaching, mobile and recovery states |
| Reliability, security and accessibility | 15% | Ownership, budgets, concurrency, recovery, keyboard/assistive access and deployment configuration |
| Verification and handover | 10% | Fresh reproducible checks, honest evidence, operational documentation and licence completeness |

Require file/line references for code findings, reproduction steps/screenshots for interface findings, severity, recommended fixes and explicit missing evidence. Save each dated review against its inspected commit in `docs/quality-review.md`. This is an independent engineering assessment, not a user-research score.

**Pass only if** the weighted overall score is at least **8.0/10**, every dimension is at least **8.0/10**, all required acceptance gates have evidence and there are no unresolved critical/high-severity defects. Do not round a score up to reach the threshold. A good average cannot compensate for broken chess logic, confusing core interactions or a security defect.

Use this exact loop: review round 1; if it fails, fix the findings, rerun affected checks and request independent round 2; if that fails, fix and verify again, then request independent round 3. Stop immediately when the pass criteria are met. **Never exceed three review rounds in total**; do not reset the counter, substitute self-review or hide failed rounds. The reviewer must recheck fixes and relevant regressions against the updated commit. If round 3 still fails, report the achieved scores, remaining issues and failed gates honestly; do not claim the goal complete or the product release-ready. If independent review cannot run, state that limitation and leave the review gate open.

## 7. Final handover

Deliver the complete source/migrations/configuration, a runnable local release, reproducible checks, evidence and independent review record, updated documentation and a short demonstration script. Summarize what changed, why, how it was verified, final quality scores/round count and any material limitations or externally blocked gates. Distinguish locally validated release preparation from an actual public deployment.

Continue autonomously through routine technical decisions. Do not stop at a plan or declare completion merely because a feature exists in code. The goal is complete only when the full required coaching loop works, all acceptance gates pass and the independent quality threshold is met within the review limit.