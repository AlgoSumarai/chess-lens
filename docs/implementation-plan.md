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

All required vertical slices and release tooling are implemented, with dated native/ARM64, recovery, security, accessibility, licence/source, restore/cutover, benchmark and clean-checkout evidence in [verification](verification.md). Independent Round 1 against 9cbcc22 scored 8.355/10 but failed the required malformed-clock mixed-import and exhausted-recovery gates. Its exact report is retained in [quality review](quality-review.md). Corrections pass 59 focused and 227 full backend checks, 34 native desktop/mobile cases and six fresh target-ARM64 cases. Operational, licence/material, live-provider, copy-upgrade and repeat measurement evidence is current. Corrected artifact verification and independent Round 2 remain required. One of at most three review rounds has completed. The dated sections below are historical delivery records, not the current completion state.

## Goal execution — 13 October 2026

The supplied baseline was rechecked at clean `main` / `42648a5`. The complete original request is retained in [goal-requirements.md](goal-requirements.md), with a requirement-by-requirement [acceptance matrix](acceptance-matrix.md). No scope is removed by this plan.

Fresh baseline: locked .NET restore, 58 backend checks including real Stockfish, three component checks/build, and six desktop/mobile Compose journeys. The pre-existing database volume rejected the local `.env` credentials; its credentials and data were left intact. Verification uses a separate Compose project/database at port 54339 and loopback app port 8088.

Provider imports/resync and durable bounded batch review are now implemented with two additive migrations. Contract/pipeline tests pass, and two games per provider import and resync through the actual Compose worker. Expanded browser verification is in progress. Next delivery order: grounded no-key/optional model coaching; conversion/endgame rules; mistake sessions; timezone reports/progress; integrated accessibility/recovery/release evidence and handover. Independent review is reserved until the whole integrated implementation and evidence are ready; zero rounds have been used.

## 13 October — coaching, detectors and practice

Grounded three-level coaching and v2 conversion/endgame detectors are implemented and committed at `76de5cd`, with fresh backend/component/Compose evidence. Persistent mistake practice now has a versioned migration, bounded full-strength objective grading, separate practical replies, assistance/history/attempts and connected exact-position/weakness links. All eighteen integrated desktop/mobile journeys pass on the ready v2 practice Compose build. Backend and affected rule checks, including the observed practical branch, pass; exact evidence and retained earlier failures are in the ledger. Next: timezone reports/progress, then integrated recovery/accessibility/release evidence and the independent review. Zero review rounds used.

## 13 October — reports and progress

The reports/progress vertical slice adds persistent calendar intervals, dataset/settings revisions, scheduler/backfill, quotas, cancellation/deletion barriers, bounded aggregation, independent provider/pool/profile groups, coverage-aware prior-period comparisons and connected screens. All 186 integrated backend checks, 44 focused report checks and 10 component checks pass. Four targeted desktop/mobile Compose journeys pass: the complete learning loop with real Stockfish and training shown in progress, and saved weekly reports using explicitly labelled retrospective completion timestamps. The full browser regression and remaining release/recovery/security/accessibility/architecture/licence/benchmark evidence precede independent review. Zero rounds used.


## 13 October — Engine operations and recovery

Reports/progress and private PGN isolation are committed. Retained/shared work admission and database-coordinated engine slots are committed at `291a1e8` and `388935f`. Subsequent validation uses native Docker Engine 29.9.0, as directed by the user, with separate new volumes. Worker/engine readiness and safe structured logs now pass 206 backend checks; actual worker SIGKILL, database outage/restart and unchanged five-minute lease recovery preserve the observed saved review prefix and complete 31 plies without duplication. Native restored-copy migration preserves core data digests. Remaining release/accessibility/security/mixed-load/architecture/licensing/CI/clean-checkout gates and independent review stay open. Zero review rounds used.
