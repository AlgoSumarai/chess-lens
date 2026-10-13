# ChessLens implementation and verification plan

The authoritative scope is [the full build brief](build-brief.md). All six milestones remain required. This document records delivery order, not a reduced target.

## Assumptions

- Repository was empty on 2026-09-13. No inherited code conventions or AGENTS.md were found.
- Build a local, reviewable modular monolith with an independent CPU worker. Do not publish or provision paid services.
- No paid service is required. Run PostgreSQL, authentication, scheduler and Stockfish ourselves. Coaching templates are a first-class mode; an optional model provider must not decide chess facts.
- Same-origin cookie authentication. Production requires HTTPS; loopback development permits HTTP. Persist Data Protection keys.
- Original source PGNs remain immutable. An owner association selects the player's colour; ambiguous PGNs remain viewable but ineligible for personal analytics until selection.
- Dark ink surfaces, cream board, restrained amber accent. The board and today's evidence-backed practice are the visual focus. No decorative generated imagery.
- Public usernames are unverified identities. Import only completed standard games. Missing data stays null.
- Per-user import/engine quotas and globally bounded worker resources are part of the implementation, not deployment advice alone.

## Milestones and evidence gates

| Milestone | Required implementation | Evidence required before completion |
| --- | --- | --- |
| 1 Foundation | Identity cookies/CSRF, preferences, PostgreSQL/migrations, bounded mixed PGN import, ownership, duplicate handling, game library, legal replay and variation validation | Rules fixtures including castling/en passant/promotion/custom FEN; PGN comments/variation/mixed errors/identity/dedup tests; account and cross-owner HTTP tests; browser register/import/replay |
| 2 Analysis | Durable leased jobs, supervised Stockfish worker, history-aware evaluations, score/mate semantics, versions/profiles, SignalR reconciliation | Real engine protocol suite; White/Black/mate/draw-history tests; cancellation/crash/restart/repeated delivery tests; reconnect journey |
| 3 Coaching loop | First four evidence detectors, denominators/coverage, stable puzzles/alternatives, factual coaching | Labelled recurring-error fixtures; insufficient evidence and clock coverage; deep puzzle validation and acceptable alternatives; linked board positions |
| 4 Imports and AI | Public providers, retries/cache/resync/cancellation, grounded optional LLM with budget/output validation | Provider contract fixtures and live bounded imports; failure states; injection/invalid moves/provider failures and no-key mode |
| 5 Improvement tools | Mistake practice, assisted/unassisted grading, spaced review, timezone weekly reports, comparable progress | Legal engine replies, history and objective grading; attempts/scheduling; local-calendar boundaries/idempotence/snapshots; comparable denominators |
| 6 Release | All connected responsive screens, demo isolation, deployment, documentation, locks, security/recovery checks, benchmark | Clean startup, automated critical desktop/mobile journeys, keyboard checks, owner isolation and quotas, documented measured engine resources, full requirement audit |

## Architecture

`ChessLens.Core` contains accounts, games/imports, analysis, insights, training and reports modules plus EF persistence. `ChessLens.Api` exposes owner-scoped HTTP and SignalR contracts. `ChessLens.Worker` runs background processing using the same domain/application code. `web` is React/TypeScript/Vite; a reverse proxy supplies one browser origin. PostgreSQL is authoritative for job state, source games and versioned derived results. SignalR messages only notify clients to reconcile persisted state.

## Initial environment evidence

- Windows 11, .NET SDK 10.0.203 / runtime 10.0.7 and Node 24.15.0 available.
- Docker CLI present; engine initially stopped. Ubuntu WSL distribution present.
- Production runtime must be patched to the selected current .NET 10 servicing version before release. Development machine versions do not prove production support.

## Status

Foundation, mainline/interactive analysis, core weakness snapshots and personal puzzle training are implemented, with backend/component/desktop/mobile container checks. Puzzle attempts retain hints/outcomes and spaced-review dates; real engine fixtures check short mating and material objectives. Draw history, compatible caching, partial-review recovery, versioned classification and coherent MultiPV batches have regression coverage. Provider imports, grounded coaching, conversion/endgame detectors and persistent mistake practice now have fresh slice evidence below. Reports/progress and release work remain required. No release gate is claimed complete. See `docs/verification.md` for actual evidence and limitations.

## Goal execution — 13 October 2026

The supplied baseline was rechecked at clean `main` / `42648a5`. The complete original request is retained in [goal-requirements.md](goal-requirements.md), with a requirement-by-requirement [acceptance matrix](acceptance-matrix.md). No scope is removed by this plan.

Fresh baseline: locked .NET restore, 58 backend checks including real Stockfish, three component checks/build, and six desktop/mobile Compose journeys. The pre-existing database volume rejected the local `.env` credentials; its credentials and data were left intact. Verification uses a separate Compose project/database at port 54339 and loopback app port 8088.

Provider imports/resync and durable bounded batch review are now implemented with two additive migrations. Contract/pipeline tests pass, and two games per provider import and resync through the actual Compose worker. Expanded browser verification is in progress. Next delivery order: grounded no-key/optional model coaching; conversion/endgame rules; mistake sessions; timezone reports/progress; integrated accessibility/recovery/release evidence and handover. Independent review is reserved until the whole integrated implementation and evidence are ready; zero rounds have been used.

## 13 October — coaching, detectors and practice

Grounded three-level coaching and v2 conversion/endgame detectors are implemented and committed at `76de5cd`, with fresh backend/component/Compose evidence. Persistent mistake practice now has a versioned migration, bounded full-strength objective grading, separate practical replies, assistance/history/attempts and connected exact-position/weakness links. All eighteen integrated desktop/mobile journeys pass on the ready v2 practice Compose build. Backend and affected rule checks, including the observed practical branch, pass; exact evidence and retained earlier failures are in the ledger. Next: timezone reports/progress, then integrated recovery/accessibility/release evidence and the independent review. Zero review rounds used.
