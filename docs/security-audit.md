# Authorization, deletion and resource audit

Inspected 15 October 2026 against application commit `bee92d0`; the additional
all-job SignalR test is recorded separately in the verification ledger. This is
an engineering audit with PostgreSQL/HTTP/SignalR fixtures and the named live
checks, not a penetration-test certification.

| Boundary | Implementation and invariant | Verification |
| --- | --- | --- |
| Accounts and preferences | `Api/AccountsEndpoints.cs`: authenticated principal supplies owner; registration/login require request CSRF; logout invalidates the session | `AccountImportTests`; `ProductionBoundaryTests`; actual HTTPS registration/restart |
| Imports, profiles, identities and batch review | `GameEndpoints`, `ImportBatchEndpoints`: every lookup and command scopes owner; provider profiles are unverified; imports cannot choose network endpoints | `AccountImportTests`; `ProviderContractTests`; `ProviderPipelineTests`; bounded live provider resync |
| Mainline and position jobs/channels | `AnalysisEndpoints`, `PositionAnalysisEndpoints`: source association and channel owner checked; ordered navigation rejects stale commands; full legal history validated | `AnalysisPipelineTests`; `PositionAnalysisTests`; actual API outage and worker/engine/database faults |
| Saved weaknesses | `InsightEndpoints`: owned snapshots and source selection; saved result contains frozen run references/settings | `InsightSnapshotTests`; private clock/PGN isolation; exact-position browser journey |
| Puzzles and attempts | `PuzzleEndpoints`: owned source run/colour/evidence; attempts are scoped independently; answers withheld before play; version/command guards | `PuzzlePipelineTests`; `PuzzleValidatorTests`; real alternatives/hints/playback journeys |
| Coaching | `CoachingEndpoints`: owned decision before constructing minimal evidence; validated evidence/prompt/model cache identity; keys remain server-side | `CoachingPipelineTests`; injection/illegal/stale/motif fixtures; no-key browser journey |
| Practice sessions, attempts and jobs | `PracticeEndpoints`: owned run and saved weakness; verified original colour; attempt/version checked under owner lock; answers hidden during play | `PracticePipelineTests`; `PracticeRulesTests`; concurrent duplicate commands; actual Stockfish practice |
| Reports, progress and scheduler | `ReportEndpoints`, `ReportCapture`, `ReportScheduler`: principal owner, retained immutable inputs and epoch check before enqueue; scheduler leases owned records | `ReportPipelineTests`; calendar/filter/comparison fixtures; actual deep-review comparison and saved-report browser journeys |
| SignalR | `ProgressHub`: authenticated connection joins only its principal's group; explicit subscription checks all nine job/session entity kinds; broadcaster sends only to each record's owner group | `RealtimeTests`: real long-poll connections, foreign subscription rejection and delivery isolation; actual WebSocket interruption/reconnect |
| CSRF | `Program.cs`: every non-GET/non-HEAD `/api` request validates antiforgery before endpoint execution, including anonymously reachable account/demo mutations | Missing-token account, practice/report and production tests; actual HTTPS rejection; no per-entity exemption |

Paths in the table are relative to `src/ChessLens.Api` unless a Core class is
named. Test paths are under `tests/ChessLens.Tests`. The hub fixtures establish
authorization and deletion behavior; they do not execute fabricated chess
evidence. Actual chess behavior has separate engine/browser evidence.

Data deletion acquires the same owner transaction lock as commands/admission,
increments the user's data epoch, deletes reports/schedules/imports/insights,
then removes owned game associations/profiles and unreferenced canonical games.
Composite association and decision foreign keys cascade analysis, interactive,
coaching, puzzle/attempt and practice/attempt/job records. Shared canonical games
remain while another owner references them; private annotations and clocks live
on the owner association. Legacy unattributed content is withheld.

Processors publish with a conditional update on the original job ID, active
state and lease token. Mainline checkpoint insertion locks/rechecks its parent
run before adding a move. Practice additionally rechecks session version and
attempt; provider delivery rechecks its parent job/lease under a row lock;
report capture rejects a changed epoch. Deleting the parent makes late updates
affect zero rows. The cancellation/deletion callback tests use labelled doubles
to select exact race boundaries. They supplement actual sampled process faults.
Retained daily resource/cost accounting remains intentionally attached to the
account; deleting imported work does not reset allowance or recreate content.

`ResourceAdmission` serializes owner and shared admission in PostgreSQL, with a
1,000-pending-work ceiling across queues and retained rolling-day budgets.
`EngineSlots` supervises at most two healthy-session processes per application
database; local pool and Compose CPU/memory limits also apply. Provider requests
are allowlisted, serial, cached and bounded. Optional model requests reserve
request/cost allowance before transmission; cancellation or deletion cannot
publish their late explanations. See [exact limits](resource-limits.md) and the
documented network-partition boundary. Atomic capacity fixtures, actual shared
engine-slot faults and measured mixed workloads have distinct evidence entries.

Production requires exact HTTPS origins/hosts and an explicit trusted proxy.
The edge overwrites forwarded headers, rejects foreign hosts, uses TLS and HSTS,
and passes no forwarded host. Secure host-only HttpOnly SameSite=Strict cookies,
persistent protection keys and distinct file-mounted runtime/migration/admin
credentials are exercised by `check-https.py`. The runtime role has DML access
without superuser, role/database creation or schema creation. Development HTTP is
limited by the documented loopback Compose binding.

`JobExecution`, worker and broadcaster logs record correlation/job/version,
duration and error type. They do not intentionally log raw PGNs, comments,
usernames, keys or connection strings. Provider-facing and model-generated text
is untrusted input. Logging fixtures and sampled native JSON logs are recorded
in the ledger; no claim is made that arbitrary third-party exception text is
safe to log.
