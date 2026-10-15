# Independent quality review — Round 1 of at most 3

**Date:** 15 October 2026, independently inspected approximately 02:27–02:45 UTC (Africa/Johannesburg: 04:27–04:45).

**Decision: FAIL. Weighted score: 8.355/10.** Every dimension meets 8.0, and no critical/high defect was identified, but required mixed-import and actionable terminal-failure behavior has reproducible counterexamples. The numerical average cannot close those gates. Preserve this as failed Round 1; fix and verify the findings before independent Round 2. This is an engineering assessment, not user research.

## Identity, independence and scope

Inspected clean `main` at **`9cbcc22b00544e8329745a910aea8fbfa34f9987`**, against original baseline `42648a584d62482bac0651438f0f36dd617863c3`. I did not author the implementation, changed no application/test/documentation source, and spawned no reviewers. Review scripts, probes and receipts were written only under `.local/evidence/review/`. Git status remained empty. The original attached requirements were read first, followed by `docs/build-brief.md` and `docs/goal-requirements.md`; there was no applicable `AGENTS.md`.

The original diff, new modules and relevant complete code were inspected: provider transport/checkpoints/store, coaching evidence/structured output/budgets, objective detectors, full-history practice/grading, report capture/calendar/scheduler/comparability, resource admission/engine slots, HTTP/SignalR ownership, deletion, health/logging, persistence mappings and migrations. The review also examined tests and the acceptance matrix, README, verification, security audit, analytics/coaching/practice/report rules, provider documentation, accessibility, dependencies/licences, benchmark, CI, release/record, operations, backup and deployment documentation.

The actual UI was the **exported, offline-started release at `http://localhost:8095`**. Backend tests used unique test databases on PostgreSQL port 54339. Docker operations explicitly used native Engine's `default` context through the existing Docker group; no Desktop, socket-permission change, service fault, existing-user data deletion, paid model call or publication occurred. Read-only HTTPS probes used the supplied private CA on port 8443 without changing trust stores.

I independently confirmed that runtime inputs in `src`, `web`, Docker/configuration and licence inputs are unchanged between application commit `bee92d0c659a9476ec315820467193f394968be0` and the inspected commit. The existing source archive is explicitly `880808b`, earlier than the final evidence/review documentation. It must not be described as containing this review; final handover remains the author's subsequent packaging step.

## Findings requiring correction

### R1 — P2 / medium: one malformed optional PGN clock prevents later valid games from importing

**References:** `src/ChessLens.Core/Imports/PgnReader.cs:103–106`; `src/ChessLens.Core/Imports/ImportProcessor.cs:78–84` and `:98–111`.

The clock regex accepts an unbounded hours field, then `int.Parse` throws `OverflowException`. The per-game boundary catches only `ArgumentException`. The worker retries the entire offending record four times, never advances to the following valid game, then fails the job. This is a concrete violation of importing valid games from a mixed batch and reporting per-game issues. It is confined to the submitted batch; no cross-owner disclosure or destruction of existing games was observed.

**Actual UI reproduction on the exported release:** register a fresh synthetic account; paste the following two completed games; select `Learner`; submit and wait for the durable terminal state:

```pgn
[Event "Malformed optional clock"]
[White "Learner"]
[Black "Opponent"]
[Result "0-1"]
1.f3 {[%clk 999999999999999999999:00:00]} e5 2.g4 Qh4# 0-1

[Event "Valid following game"]
[White "Learner"]
[Black "Opponent"]
[Result "0-1"]
1.f3 e5 2.g4 Qh4# 0-1
```

**Observed:** state `failed`, imported/processed/total all zero, empty per-game failures, empty library. Repeated independently twice. The second reproduction waited for the rendered `FAILED` badge as well as the HTTP state. Both synthetic accounts' imported data was deleted afterward and empty libraries verified.

**Evidence:** `.local/evidence/review/round1/malformed-ui.{cjs,json,log}`; `.local/evidence/review/round1/malformed-clock-mixed-import.png`. First reproduction is retained in `round1/malformed-first/`.

**Recommended fix:** parse optional clocks with bounded, nonthrowing numeric conversion and safe arithmetic; unavailable/invalid optional timing must not abort otherwise legal completed games. If choosing to reject a record, convert its validation error into the existing per-game failure path and continue to later records. Avoid unchecked hours-to-seconds integer overflow as well as oversized text. Add mixed-batch regressions for the exact reproduction, clocks exceeding Int32 seconds, preserved valid clocks, and later valid/illegal records.

**Gate implication:** reopen **IMP-01** and the mixed-failure portion of **IMP-04**. Passing ordinary malformed-SAN and missing-clock fixtures does not cover this boundary.

### R2 — P2 / medium: a wrong-type Lichess clock aborts the entire NDJSON selection

**References:** `src/ChessLens.Core/Imports/ProviderContracts.cs:130–137`; `src/ChessLens.Core/Imports/ProviderImportProcessor.cs:96` and its outer retry boundary.

`TryGetDouble` throws when a JSON element is not a number; it is not a safe type test. The record loop catches only `JsonException`. A syntactically valid object with `"clocks":["bad-clock"]` therefore aborts parsing before subsequent valid records become a durable checkpoint. It produces repeated whole-import failure instead of isolating bad optional metadata or the bad record.

**Independent code execution:** the retained file-based .NET probe calls the actual `ProviderContracts.ParseLichess` with two in-range completed-game records, the first containing that clock value and the second valid. It prints:

```text
Parser aborted entire two-game batch: InvalidOperationException: The requested operation requires an element of type 'Number', but the target element has type 'String'.
```

**Evidence:** `.local/evidence/review/round1/provider-probe.{cs,log}`. This is a direct parser probe, not a claim that the real public Lichess service returned malformed clocks. Both real providers' ordinary final-release imports/resync separately passed.

**Recommended fix:** inspect `JsonValueKind` before numeric conversion, preserve invalid optional clocks as unavailable (or record a bounded per-record issue), and continue parsing the remaining selection. Add number/null/string/object clock cases with a following valid record and verify the durable worker outcome, not only the helper's return value.

**Gate implication:** reopen the malformed/mixed-record portion of **IMP-04**. Provider rate-limit/retry success is not evidence for semantic-record isolation.

### R3 — P2 / medium: an exhausted PGN job still promises an automatic retry

**References:** `src/ChessLens.Core/Imports/ImportProcessor.cs:105–106`; `web/src/Imports.tsx:91–104`.

On R1's fourth failure, the processor sets state `failed` while retaining **“Import interrupted. Retrying within the configured limit.”** The UI displays that text beside `FAILED`, reports zero per-game issues, and offers no retry action for this PGN job (`request` is null). Waiting will not resume it. This is a separate recovery-message defect even after the specific clock input is hardened.

**Reproduction/evidence:** R1's second actual UI reproduction and screenshot show the terminal badge and retry promise together. `malformed-ui.json` records the terminal state, version 8, no next attempt, and the exact error text.

**Recommended fix:** distinguish queued retry from exhausted terminal failure in persisted error text; explain that saved records remain available and give a concrete next step, such as correcting/resubmitting the PGN. Test queued versus final retry messages and an actionable rendered failure state.

**Gate implication:** **UI-04** is not fully passing for terminal PGN import failure. Existing generic failure/retry browser fixtures do not establish this specific state.

## Lower-priority observations

- **P3 / low, navigation:** progress snapshots are served under `/reports/{id}`, so `web/src/App.tsx:82` marks **Weekly reports** as the current navigation item even when the page is titled **Your progress snapshot**. Reproduce by generating progress; both `desktop-saved-progress.png` and `comparable-progress-desktop.png` show it. Select the current navigation destination using report kind or a compatible progress-specific route. This does not prevent completion.
- **P3 / low, documentation:** `docs/analytics-rules.md:48` still says reports are subsequent work, although reports are implemented and exercised. Link the current report rules and remove that stale status sentence.
- **Design improvement:** findings and mobile review contain substantial coverage/search/context material before the lesson and practice action (`web/src/Weaknesses.tsx:56–68`, `web/src/GameReview.tsx:196–248`). The evidence is useful and honest, but the primary recommendation takes considerable scrolling. Keep concise coverage beside the recommendation and put deeper diagnostics in disclosures. The inspected primary loop remained usable; this is not a broken-gate finding.

## Independently executed checks and observed interface

All commands below ran on 15 October 2026 against the inspected checkout/exported runtime. Paths are under `.local/evidence/review/round1/` unless stated otherwise.

| Check | Fresh result and evidence |
| --- | --- |
| Backend, actual PostgreSQL and real-Stockfish cases | Load `scripts/load-env.sh` with `DATABASE_PORT=54339`, SDK/runtime 10.0.402/10.0.13; `dotnet test ChessLens.slnx --no-restore --logger 'trx;LogFileName=independent-backend.trx' --results-directory .local/evidence/review/round1/backend`: **214 passed, 0 failed, 0 skipped**, 1m16s. `backend.log`, `backend/independent-backend.trx`. Includes provider/model doubles explicitly distinguished in test source; the real engine cases also execute. |
| Frontend | Node 24.22.0, `npm test` and `npm run build`: **10 component tests passed**, production build passed. `components.log`, `build.log`. |
| Own UI journey | Fresh desktop 1440×1000 and Pixel 7 touch emulation 393×851 accounts: import → deep review → verified weakness → exact saved run/ply → factual no-key coaching → solved personal puzzle → unassisted practice → retry/hint/reload/assisted result → frozen filtered progress and empty completed-week report. Keyboard replay and legal alternative exploration hid stale coaching correctly. Mobile preferences also saved. `journey.cjs`, `journey.json`, `journey-summary.json`, PNGs and traces. Both reached saved progress and own imported data was cleaned. |
| Additional actual browser cases | Against 8095: `npx playwright test e2e/comparable-progress.spec.ts e2e/practice.spec.ts --project=desktop --grep 'original artificial|saved weakness' ...`: **2 passed**, 1m. Supported descriptive comparison **5/15 (33.3%) → 0/10 (0%)**, with denominators and noncausal wording; three full-strength-graded decisions with practical opponent replies, hints, undo, reload and saved weakness link. `representative-browser.log`, `representative-report/index.html`. |
| Small mobile controls | `npx playwright test e2e/accessibility-controls.spec.ts --project=mobile --grep 'small screens' ...`: **1 passed**, 17.8s. Actual 320-pixel touch/keyboard promotion, focus/cancel, legal rook mating alternative, position text, long names and 200% text sizing. `promotion.log`, `promotion-report/index.html`, `promotion-access-mobile.png`. |
| Accessibility | Latest own journey: **33 WCAG-tagged scan observations**, zero violations and no document overflow in those ordinary states. Additional repository browser checks: **8 complete default-rule axe scans**, zero violations and no unresolved prohibited-ARIA notes (`promotion-axe.json`, `representative-axe.json`). Inconclusive contrast nodes remain documented; zero violations is not a complete accessibility certification. |
| Live public adapters | `python3 scripts/check-provider-imports.py --url http://localhost:8095 --output .../live-providers.json`: **both passed**; two real completed games each, resync zero new/two duplicates each, immutable game payloads; synthetic imported data deleted. `live-providers.{json,log}`. |
| Release integrity | `sg docker -c 'python3 scripts/release.py verify .local/releases/2026.10.15-local'`: all manifest/file hashes and **8 selected-platform runtime image IDs passed**. `release-verify.log`. This is integrity/identity evidence, not a reproducible compiler-output attestation. |
| Both worker images | `scripts/check-engine-image.py --image chesslens-release-worker:2026.10.15-local-{amd64,arm64} --platform linux/{amd64,arm64} --verify-source ...`: **174 exact binary/source/network/notice hashes and 3 actual UCI checks each passed**, clean exit. `engine-{amd64,arm64}.{json,log}`. ARM64 uses actual target-architecture execution under QEMU, not physical ARM hardware. |
| Licensing | `python3 scripts/licenses.py`: exact locks/text hashes match **30 npm and 63 NuGet entries**. `licenses.log`. |
| Current HTTPS, read-only | Actual CA/hostname readiness 200; secure host-prefixed HttpOnly Strict CSRF cookie; HSTS; ordinary untrusted client rejects private CA; foreign Host 421. `https-readonly.json`. No restart or role mutation by this reviewer. |
| Adversarial input | R1 actual mixed-import UI fails as described; R2 direct real-parser probe aborts as described. These failing checks are retained and determine the gate failure. |

I visually inspected current desktop review/coaching, weakness evidence, personal puzzle/play/review, hidden-answer practice and graded outcomes, frozen progress and supported comparisons, mobile import/review/practice/progress, mobile promotion and the malformed-import failure screenshots. The dark/warm-board visual system is coherent, controls remain readable, legal text entry works, and sample-size/assistance/insufficient-data language is unusually clear. The principal remaining interface concerns are density and the actionable terminal failure above.

**Verification-harness limitations are retained rather than hidden:** the first own script stopped at an over-exact select-label locator; the second stopped when its request context lacked `baseURL`. Both are reviewer harness faults, with earlier artifacts in `journey-first/` and `journey-second/`; no product failure is inferred. The latest script completed the desktop learning loop through saved progress and weekly empty state, then an exploratory **root CSS `zoom:2`** assertion failed (1848 versus 1440 content width). That technique does not change the browser's layout viewport like actual browser zoom, so it is explicitly excluded from the ordinary-state width/scan summary and is not recorded as a native-browser-zoom pass or an application zoom defect. It also prevented the latest desktop script reaching settings; mobile settings did complete. `journey.json` truthfully retains the desktop script's failed result. The separate 3 repository browser cases above are terminal passes. Some immediate route snapshots captured a transitional previous view; those are not chosen as visual handover evidence.

## Audit of pre-existing evidence and remaining limits

The assessment did not simply accept the ledger's counts. In addition to fresh execution, I inspected the named raw receipts, tests and relevant implementation for: actual worker/engine/database faults and unchanged leases (`recovery/native-worker-database.json`, `recovery/lease-exhaustion/actual-leases.json`); actual Orca generated speech including objective-completion announcements and native 200% browser zoom (`accessibility/orca-status.log`, `manual-training-announcement.log`, native screenshots); clean-clone empty final status and 58-scan receipt; final mixed benchmark raw events/search evidence; all-table/key restore and copied-release cutover; native/ARM source/UCI and notice inventories. Those are **author-performed actual experiments that I audited**, not experiments I repeated. No new fault injection was authorized for this review.

The raw benchmark supports the documented finite 146.169-second six-review/182-ply, six-import/five-side-job workload and queue-progress claim; it does not establish arbitrary-load latency or production capacity. The original failed quota fixture remains visible. Restore/cutover receipts verify all 33 table hashes and retained protection keys; the actual final copied-database job is distinct from source-database preservation. Runtime input equality and the fresh final-release checks support applying those earlier unchanged-code observations to this candidate within their recorded boundaries.

Official primary sources were also reopened on this date: [Lichess export specification](https://raw.githubusercontent.com/lichess-org/api/master/doc/specs/tags/games/api-games-user-username.yaml), [Lichess request/rate guidance](https://lichess.org/page/api-tips), [Chess.com API](https://www.chess.com/news/view/published-data-api), [.NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), [Node 24.22.0 release](https://nodejs.org/en/blog/release/v24.22.0), and [nginx security advisories](https://nginx.org/en/security_advisories.html). The selected Lichess parameters/serial one-minute rate-limit behavior and nginx servicing rationale agree with the inspected contracts. An advisory feed is not proof against unknown vulnerabilities.

**Explicit missing evidence:** passing regression evidence for R1/R2's malformed optional-clock boundaries and R3's exhausted-state recovery message does not exist; fresh executions fail. This is a required local acceptance gap, not an external blocker. The acceptance matrix's broad verified import/failure rows must be corrected until the fixes and tests pass. Final reviewed-source handover is still subsequent work. Hosted GitHub Actions, physical Windows/native ARM devices, NVDA/VoiceOver/braille, live paid model delivery, public deployment and remote database TLS remain untested/not claimed. The optional model is validated here through structured-provider fixtures and useful actual no-key operation. The existing actual Orca/browser-zoom inspection was audited, not independently repeated with those native tools.

## Scores and exact decision

| Dimension | Weight | Score | Basis |
| --- | ---: | ---: | --- |
| Code quality and maintainability | 25% | **8.3** | Focused module boundaries, typed contracts, additive/versioned persistence, shared admission/evidence validation and meaningful tests. Numeric parsing escapes intended validation boundaries; dense endpoint/UI expressions and string states increase maintenance cost. |
| Chess/domain and whole feature scope | 25% | **8.4** | Broad completed-game loop is real: legal/history-aware engine work, conservative objective evidence, supported alternatives, separate grading/opponent settings, immutable reports/comparable denominators and no-key coaching. Both real providers work normally. Mixed-import counterexamples prevent full required-scope acceptance. |
| Ease/interface quality | 25% | **8.1** | Actual desktop/mobile loop works, warm board/readable controls, clear evidence and assistance labels, usable keyboard/touch alternatives and sound empty states. Dense pages, wrong active nav item for progress snapshots and misleading terminal retry text reduce clarity. |
| Reliability/security/accessibility | 15% | **8.5** | Fresh ownership/realtime/deletion/resource tests, real engine checks, actual audited recovery/TLS/restore receipts and fresh small-screen/default axe checks. Malformed input recovery still needs correction; physical/assistive/platform limits are disclosed. |
| Verification/handover | 10% | **8.8** | Extensive scoped receipts plus independent full backend/components/build, both images/materials, actual provider resync, UI and HTTPS probes. Licence/source material and operations are strong. Final handover/review status is pending, some current prose is stale, and new negative cases expose overbroad verified labels. |

Arithmetic, without rounding up:

`8.3×0.25 + 8.4×0.25 + 8.1×0.25 + 8.5×0.15 + 8.8×0.10`

`= 2.075 + 2.100 + 2.025 + 1.275 + 0.880 = 8.355/10`.

- Each dimension at least 8.0: **yes**.
- Weighted result at least 8.0: **yes**.
- No unresolved critical/high identified: **yes**, within this engineering review's scope.
- Every required acceptance gate passing: **no** — mixed-record import isolation and actionable exhausted-import recovery fail; final handover remains pending.
- **Round 1 result: FAIL. Do not declare release-ready or the goal complete.** After correcting R1–R3, rerun affected parser/pipeline/UI checks and relevant integration regressions, rebuild changed runtime images and request independent Round 2. Do not reset this round count.

## Suggested shareable evidence subset

These contain synthetic game/account labels and no session cookies, tokens, keys or private user games. Full traces, test credentials/scripts, Orca logs and backups should remain private under `.local`.

- `round1/malformed-clock-mixed-import.png` — required-gate failure, rendered final state.
- `round1/desktop-review-coaching.png` — saved decision, board/notation/evaluation/factual lesson.
- `round1/mobile-practice-unassisted-complete.png` — actual mobile completed practice.
- `round1/practice-three-decisions-desktop.png` — actual opponent continuation and bounded grading.
- `round1/comparable-progress-desktop.png` — supported descriptive comparison with denominators.
- Summary receipts: `round1/journey-summary.json`, `promotion-axe.json`, `representative-axe.json`, `https-readonly.json`, and `engine-{amd64,arm64}.json` (the latter include harmless UCI transcripts); a concise aggregate can accompany the report. The failure receipt `malformed-ui.json` contains only a synthetic email/job state, but omit its email if producing a public summary.
