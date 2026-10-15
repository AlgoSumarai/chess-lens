# Independent quality review record

**Final assessment: PASS at Round 2, 8.545/10, 15 October 2026. Two of at most three rounds used; the review loop stopped.** Every dimension exceeds 8.0; the separate non-author reviewer found all required local engineering gates evidenced and no unresolved critical/high defect. Final handover and exact source packaging are recorded in [the handover](handover.md) and [release record](release-record.md).

| Dimension | Weight | Round 1 — FAIL | Round 2 — PASS |
| --- | ---: | ---: | ---: |
| Code quality/maintainability | 25% | 8.3 | 8.5 |
| Chess/domain/feature correctness | 25% | 8.4 | 8.7 |
| Ease/interface quality | 25% | 8.1 | 8.2 |
| Reliability/security/accessibility | 15% | 8.5 | 8.7 |
| Verification/handover | 10% | 8.8 | 8.9 |
| **Weighted score** | **100%** | **8.355** | **8.545** |

Round 1 failed three required mixed-import/recovery boundaries despite its passing numerical scores. Round 2 independently rechecked all three corrections and the whole scope against frozen source `3b12bd712cf52a76c7d86a77f36981a971610985`; runtime inputs are `3b3ae4a808cc2dda5e1ce789ccab2da8558cbf86`. Subsequent handover changes are documentation/evidence only. No third review was requested.

Remaining P3 observations concern the active navigation highlight on saved progress and page density; they do not block the verified journey. Platform, accessibility, model and deployment limits remain explicit in the reports. [Selected safe evidence](review-evidence/README.md) accompanies exact report hashes; private traces/backups/credentials remain outside source archives.

Both dated reports below are preserved verbatim, including failures, their then-pending work and limitations. Exact standalone copies are [Round 1](review-evidence/round1/report.md) and [Round 2](review-evidence/round2/report.md).

---

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


---

# Independent quality review — Round 2 of at most 3

**Date:** 15 October 2026; independent inspection/checks approximately 10:04–10:12 UTC (12:04–12:12 Africa/Johannesburg).

**Decision: PASS. Weighted result: 8.545/10. Every dimension is at least 8.0.** The three Round 1 P2 findings are resolved with independent code, database/HTTP, parser and actual desktop/mobile evidence. No unresolved critical/high defect or required engineering acceptance gap was identified. **Stop the review loop here; no Round 3 is required.** Round 1 remains **FAIL, 8.355/10**, and is not superseded or removed from history.

This is an independent engineering assessment of the complete required local application and release preparation, not user research, a security certification or a public-deployment claim.

## Inspected state and independence

- Frozen, clean `main`: **`3b12bd712cf52a76c7d86a77f36981a971610985`**.
- Previous independently inspected commit: `9cbcc22b00544e8329745a910aea8fbfa34f9987`; original baseline remains `42648a584d62482bac0651438f0f36dd617863c3`.
- Corrected runtime source: **`3b3ae4a808cc2dda5e1ce789ccab2da8558cbf86`**. I independently checked that subsequent changes to the inspected commit do not change runtime application/configuration/licence inputs.
- Actual browser target: **`http://localhost:8098`**, the freshly started exported `2026.10.15-r2` release, project `chesslens-review2-offline-validation`. The packet's explicit source snapshot is `792a4753e14a78bad4a711f4c1ed6f74b4888378`; it does not pretend to contain later review results.
- The original attached requirements and repository goal/build brief were reread. Scope was not reduced to the three fixes. The full-scope code/migration/test/documentation assessment from Round 1 was retained, then the entire intervening diff, relevant complete import code/tests, updated matrix, verification, handover, rules, benchmark and release evidence were inspected.
- I remain the separate non-author reviewer, changed no tracked application/tests/docs, and spawned no agents. Tests used uniquely named PostgreSQL databases on the supplied test server. Docker checks used native Engine's `default` context through the existing Docker group. No unrelated stack was faulted/reconfigured, existing-user data deleted, paid model invoked or external publication performed.
- `docs/quality-review.md` and the original `.local/evidence/review/round1.md` have the same SHA-256 (`d54819d23c80c0240424dab9ac5eded638904dacca4435f00f1a895c6b3ad62c`), confirming the failed round was preserved verbatim. No schema/migration change occurred after Round 1.

## Round 1 findings rechecked

| Finding | Corrected implementation and independent evidence | Result |
| --- | --- | --- |
| **R1, P2: oversized optional PGN clock aborts a mixed batch** | `src/ChessLens.Core/Imports/PgnReader.cs:100–109` now uses invariant nonthrowing conversion, finite/range checks and floating-point hours-to-seconds arithmetic. The exact oversized hours annotation stays in the owner's raw PGN while timing becomes null. My actual desktop **and touch-mobile** imports each contained that game, an illegal second game, and a legal third game with a valid clock. Both saved two legal games, identified game 2 as illegal, retained null versus 300-second clocks, and preserved source text. Reimport produced two duplicates and no extra library entries. All seven new PGN primitive cases and the mixed HTTP/persistence regression pass independently; the positive 3,600,000,000.5-second case proves integer-seconds overflow is avoided. | **Closed** |
| **R2, P2: wrong-type Lichess clocks abort NDJSON** | `src/ChessLens.Core/Imports/ProviderContracts.cs:130–131` checks `JsonValueKind.Number` before `TryGetDouble`, then rejects negative/nonfinite values as missing optional timing. I reran the **unchanged former failing parser probe** against current code: `Parsed 2; failures 0`. New contract tests cover string/null/object/bool/negative/infinite values, and `ProviderPipelineTests.cs:90–117` exercises actual durable checkpoint/validation/database writes around two completed records using a clearly labelled provider-transport fixture. Those tests pass in my full backend run, including the one-fetch checkpoint assertion and available clocks in later records. This is not a claim that the live public provider returned malformed data. | **Closed** |
| **R3, P2: exhausted PGN job promises automatic retry** | `src/ChessLens.Core/Imports/ImportProcessor.cs:12–13`, `:21–22`, `:108–110` distinguish queued retry from exhausted stopped state and give a resubmission step. `src/ChessLens.Api/GameEndpoints.cs:133–139` presents the exact legacy failed retry text truthfully without rewriting the stored historical error. Independent PostgreSQL/HTTP cases verify queued versus fourth-attempt state and both single/list legacy projections. Two separately executed desktop/mobile **labelled response-fixture** browser cases render `FAILED`, the stopped/resubmission explanation and usable PGN submission controls, with no automatic-retry promise. Actual processor behavior is established by the database tests, not falsely attributed to that browser fixture. | **Closed** |

The independent TRX contains **all 13 added correction cases passed**, not merely an aggregate assertion. See `round2/backend-summary.json` for their names/outcomes. The stale reports-as-future-work sentence in `docs/analytics-rules.md:48` is also corrected and links current report rules.

## Fresh independent execution

All paths below are under `.local/evidence/review/round2/` unless stated otherwise. All checks ran on 15 October 2026 against the frozen checkout or exact exported runtime above.

| Check | Command/result and retained evidence |
| --- | --- |
| Full backend | SDK/runtime 10.0.402/10.0.13; load `scripts/load-env.sh` with `DATABASE_PORT=54339`; `dotnet test ChessLens.slnx --no-restore --logger 'trx;LogFileName=independent-round2.trx' --results-directory .local/evidence/review/round2/backend`: **227 passed, 0 failed, 0 skipped, 58 seconds**. Actual PostgreSQL and real Stockfish cases execute alongside explicitly labelled provider/model/pipeline doubles. `backend.log`, `backend/independent-round2.trx`, `backend-summary.json`. |
| Frontend | Node 24.22.0, `npm test` and `npm run build`: **10 components passed**; production TypeScript/Vite build passed. `components.log`, `build.log`. |
| Former Lichess counterexample | Actual file-based .NET probe against current Core: **both records parsed**, no record failures or exception. `provider-probe.cs`, `provider-probe.log`. |
| Own complete actual UI sequences | `PLAYWRIGHT_BROWSERS_PATH=.local/ms-playwright node .local/evidence/review/round2/journey.cjs`: **2 complete sequences passed**, desktop 1440×1000 and Pixel 7 touch emulation 393×851. From **10:06:37 to 10:09:27 UTC**. Includes the mixed-clock/legal/illegal import and duplicate reimport, real deep Stockfish review, verified weakness and exact saved run/ply, no-key factual coaching, legal variation with stale coaching hidden, personal puzzle, unassisted practice, retry/hint/reload/assisted practice, frozen filtered progress/reload, honest weekly empty state, supported early-signal recommendation and saved preferences. Mobile solving/practice used real tap input on queen/destination squares; desktop also exercised keyboard replay/coordinate alternatives. Both own imported libraries were deleted afterward and verified empty. `journey.cjs`, `journey.json`, `journey-summary.json`, individual screenshots/full axe JSON and private traces. |
| Accessibility in those UI states | **34 full default-rule axe scans**, no excluded rules/elements: **0 violations, 0 unresolved prohibited-ARIA notes, 0 document-content overflow observations, 0 page errors**. Inconclusive contrast nodes remain visible in raw scans and do not become a fabricated all-rules pass. |
| Terminal recovery rendering | `CHESSLENS_URL=http://localhost:8098 npx playwright test e2e/imports.spec.ts --grep 'exhausted PGN' ...`: **2 passed**, 9.8 seconds; desktop and mobile. **2 more full default-rule axe scans**, zero violations/prohibited-ARIA notes. This is explicitly labelled response-fixture UI evidence; actual queued/exhausted/legacy HTTP behavior is covered independently above. `terminal.log`, `terminal-report/index.html`, `terminal-axe.json`, `exhausted-pgn-{desktop,mobile}.png`. |
| Corrected release integrity | `sg docker -c 'python3 scripts/release.py verify .local/releases/2026.10.15-r2'`: **all release file hashes and 8 selected-platform runtime image IDs passed**. `release-verify.log`. This checks the actual corrected packet, not the failed Round 1 packet. |

No reviewer harness failures occurred in this round. The prior round's locator/baseURL failures and excluded root-CSS-zoom experiment remain preserved in its record. Root CSS zoom was not rerun or counted as browser zoom evidence.

I visually inspected the current mixed-import results, desktop board/notation/coaching, saved weakness/decision, puzzle and practice states, frozen progress and recommendation, mobile completed practice/recommendation and terminal recovery screenshots. The board, selected decision, explanation and exact source links remain coherent. Missing analysis in the extra valid game is disclosed as **1/2 completed-review coverage** in progress; the app does not silently treat it as analysed. The actual no-key lesson, per-game issue, sparse-sample warning and assisted/unassisted distinction are clear.

## Full-scope acceptance and evidence audit

This result covers the whole original target. It does not infer full scope from three parsing fixes or from author test totals alone. The earlier independent implementation/migration/security/domain review remains applicable because only the four small import/API runtime files changed; the current full suite and actual exported-release sequences provide fresh regression evidence. I checked the changed test boundaries against their implementation and the current release/documentation claims against raw receipts.

| Required area | Evidence basis and scope in this assessment |
| --- | --- |
| Accounts, owner isolation, legal PGN/replay, durable mainline/interactive engine and history-aware chess | Earlier independent full-code review plus current **227-case independent PostgreSQL/HTTP/real-engine suite** and actual desktop/mobile source-to-training flow. Existing rules/migrations, cache, legal-history, mate, alternatives, stale requests, ownership, all-nine-kind hub, deletion and quota checks are unchanged and pass. |
| Provider imports/resync and bounded batch analysis | Corrected parser/HTTP/durable tests independently execute; actual own mixed imports and deduplication pass. Current author-performed `fixes/live-providers.json` is clean committed 3b3ae4a and records **both real adapters: two imported, then zero new/two duplicate**, immutable source checks and cleanup. Round 1's independent live adapters also passed. Provider fixtures remain labelled; malformed upstream clocks were not represented as a real external event. |
| Grounded coaching and six weakness categories | Unchanged evidence/version/settings architecture and conservative motifs reviewed in Round 1; full current regression passes, including three no-key levels, injection/illegal/unrelated/stale claims, conversion/endgame boundaries and real-engine cases. Actual no-key coaching and exact weakness links work in both fresh UI profiles. Optional paid live delivery is not required for the useful core and is not claimed. |
| Personal puzzles, mistake practice, reports/progress | Actual fresh puzzle and assisted/unassisted practice persist/reload correctly. Current full regression covers full-history draws, promotion, alternatives, three-decision grading, calendar/DST/idempotence/frozen revisions/compatibility and denominators. Own progress demonstrates sparse/partial coverage honestly. Current clean-clone full browser evidence also exercises supported descriptive comparisons, three-decision opponent play, promotions, weekly retrospective fixtures and reconnection; these are unchanged rules already independently exercised in Round 1. |
| Accessibility and complete interface | **36 new independent full axe scans** and whole desktop/touch-mobile sequences, plus current author-performed **64 native/40 ARM64 clean scans** and small-control cases. The exact unchanged UI source retains the previously audited actual Orca/AT-SPI/keyboard and native 200% browser-zoom receipts; these were not independently repeated with native assistive tools in Round 2. |
| Recovery, security, resource limits and fairness | Current full independent tests pass. Earlier actual OS worker/engine/database faults, four unchanged leases, missed-notification recovery, log privacy and fencing limits remain explicitly scoped and audited from Round 1. Current benchmark raw JSON is clean committed **3b3ae4a**, **222.009 seconds, 6 reviews/182 plies, 6 imports, 110 samples**. I independently extracted all five distinct side-lane observations and verified both import/review backlogs remain positive at every observation (`round2/benchmark-audit.json`). Hardware/profiles/cache/CPU/memory/polling limits and the failed credential-file setup are disclosed. No universal throughput or failure fencing claim is inferred. |
| Clean startup, upgrades, HTTPS, backup/restore and platform packaging | Current clean-clone CI script finishes with **empty initial/final Git status**, all steps passing: 227 backend, 10 components, build, zero flagged dependency audits, 34 native browser cases, exact image materials/UCI/notices. Current 8088 and 8095 upgrade receipts preserve **all 33 table hashes and respectively two/one keys**. HTTPS copy upgrade preserves 33 selected **and** 33 original-source tables plus its key. The current real HTTPS/cookie/origin/proxy/restricted-role/restart probe and 6/6-ply job are in the selected restored copy, absent from the original DB. I inspected those receipts; I did not rerun service faults/restarts. |
| ARM64, licensing and exact release | Current target-ARM64 source/material/UCI checks and six actual fresh-stack desktop/mobile cases execute under QEMU, with explicit startup/health/browser observation grace rather than relaxed job/lease/quota limits. Both exact image archives loaded; **1,191 release file hashes/eight platform identities** independently verify. Licence/source/artwork material and runtime notices remain bundled; no new dependency or licence input changed. Fresh extracted-source `--no-build --pull never` startup on 8098 and served-notice equality are recorded separately from the source archive identity. |

Raw current author-performed evidence inspected includes `.local/evidence/review/fixes/{clean-ci.log,clean-initial-status.txt,clean-final-status.txt,native-axe-summary.json,benchmark-summary.json,benchmark-run/benchmark.json,live-providers.json,https-engine.json,offline-start/verified.json}`, the clean clone's `.local/ci/` and browser reports, `fixes/arm64/{engine-source-uci.json,axe-summary.json,browser.log,browser-passed/}`, and the native/offline/HTTPS upgrade `verified.json` receipts. Their experiments are **audited prior work**, not relabelled as my fresh executions.

The matrix's corrected **IMP-01/IMP-04/UI-04** portions now have passing independent evidence. The remaining required implementation and scoped local verification gates have evidence. This report closes the independent assessment gate. Final handover must record the actual Round 2 result and include the report/source snapshot alongside the already verified unchanged runtime images; it must not silently relabel the earlier archive as containing later documentation. That is completion of the prepared handover, not a reason to run another review round.

## Remaining low-priority observations and explicit limits

**No unresolved P2, high or critical finding was identified.** The following Round 1 observations remain P3/low and do not obstruct the required loop:

1. **Progress active navigation:** `web/src/App.tsx:82` marks **Weekly reports** current for `/reports/{id}`, including progress snapshots. Reproduce by generating progress; `round2/desktop-saved-progress.png` shows the heading **Your progress snapshot** with the Weekly reports nav highlight. Use report kind or a compatible progress-specific route to identify the current destination. The correct page, saved result and back link work.
2. **Density:** `web/src/Weaknesses.tsx:56–68` and `web/src/GameReview.tsx:196–248` put lengthy coverage/search context ahead of some lesson/practice actions, particularly on mobile. Prefer a concise recommendation and disclosure for deeper diagnostics. The current dashboard recommendation, board controls and full primary journey remain understandable and functional; these observations are reflected in the interface score.

**No mandatory local evidence gap remains in this assessment.** Explicit untested/unclaimed contexts remain: hosted GitHub Actions execution, physical Windows/native ARM hardware and native ARM throughput, NVDA/VoiceOver/braille or physical mobile devices, live paid-model delivery, public deployment and remote-database TLS. Windows/WSL commands are documented; actual clean startup here is Linux Engine. ARM64 compatibility is actual target execution under QEMU. Automated scans and inspected speech/keyboard/zoom output are engineering checks, not accessibility certification or user research. Current fresh full dependency audits report zero flagged entries, not absence of unknown vulnerabilities. These limits preserve the supported matrix and do not substitute fixtures for unavailable live gates.

## Acceptance-ID reconciliation

This explicitly reconciles every row in the current acceptance matrix. “Pass” means the scoped implementation and evidence described above, with fixture/live/manual/emulated boundaries retained; it is not a claim that this reviewer reran every historical experiment.

| Matrix IDs assessed | Determination |
| --- | --- |
| BASE-01 | Pass: original baseline/diff inspected; current native/copy upgrades preserve table/key hashes. |
| ACC-01, ACC-02, ACC-03 | Pass: current independent account/import/identity/demo/ownership/deletion suite and actual registration/preferences; late-write race fixtures retain their labelled scope. |
| IMP-01, IMP-02, IMP-03, IMP-04, IMP-05, IMP-06, IMP-07, IMP-08 | Pass: exact R1 boundaries independently corrected; mixed and duplicate actual UI imports; current real provider/resync receipts; contract/checkpoint/cache/rate/network-boundary/batch regression tests. |
| ENG-01, ENG-02, ENG-03, ENG-04 | Pass: persisted actual reviews/UI and current full real-engine/history/mate/cache/profile/resource regressions; scoped actual faults/materials and measured workloads. |
| INS-01, INS-02, INS-03, INS-04, INS-05 | Pass: all six versioned detector/evidence/denominator/coverage/frozen-run rules retain tests and representative actual-engine evidence; actual saved weakness links rechecked. |
| PUZ-01, PUZ-02, PUZ-03 | Pass: actual validation/solving/hidden answers and current alternatives/hints/attempt/scheduling regressions. |
| COA-01, COA-02, COA-03, COA-04, COA-05 | Pass: actual factual no-key UI and current three-level/rating/grounding/injection/failure/cache/budget/provenance fixtures; no paid live model claim. |
| PRA-01, PRA-02, PRA-03, PRA-04, PRA-05 | Pass: real imported mistakes, actual practice outcomes/touch/reload/assistance, independent full history/grading/version/stale/deletion tests; unchanged three-decision opponent evidence. |
| REP-01, REP-02, REP-03 | Pass: current calendar/DST/scheduler/idempotence/revision/capture tests and frozen actual UI results; retrospective completion timestamps remain labelled fixtures. |
| PRO-01, PRO-02 | Pass: filter/group/coverage/rating/denominator/comparison regressions and current full-browser evidence; own frozen filtered progress discloses its partial/sparse sample. |
| UI-01, UI-02, UI-03, UI-04 | Pass: two fresh complete desktop/mobile sequences, exact-position/variation/coaching synchronization and corrected recovery state; remaining P3 density/navigation observations are nonblocking. |
| A11Y-01, A11Y-02 | Pass within documented engineering scope: current whole/small-viewport browser evidence and 36 fresh default scans, plus unchanged-source actual keyboard/Orca/native 200% browser-zoom receipts. No physical-device/other-AT certification. |
| SEC-01, SEC-02 | Pass: independent current ownership/CSRF/all-job hub/deletion/admission tests and audited runtime-role/proxy/cookie/cost/resource boundaries. |
| REL-01, REL-02, REL-03, REL-04 | Pass: current finite mixed-queue observations, full regression and unchanged-code actual process/lease/database/reconnect/readiness/logging evidence. No arbitrary-load or partition-proof guarantee. |
| OPS-01, OPS-02, OPS-03, OPS-04, OPS-05, OPS-06 | Pass in the declared local matrix: actual clean Linux startup/CI script, documented Windows/WSL commands, native x86 and target ARM64/QEMU, HTTPS/role/key/copy-restore/cutover receipts, exact exported artifacts. Physical Windows/native ARM and hosted Actions remain unclaimed. |
| LIC-01, BEN-01 | Pass: exact complete application/artwork/Stockfish/runtime materials; current source-specific benchmark with hardware/settings/work/counts/resources/backlogs/limits. |
| VER-01, VER-02 | Pass: fresh independent integrated checks plus current clean-clone/native/ARM evidence, explicit acceptance traceability and retained negative/history distinctions. |
| REV-01, REV-02 | Pass **at Round 2** by this separate assessment: each dimension ≥8, weighted 8.545, no unresolved critical/high, required local evidence present; failed Round 1 is retained. |
| HAN-01 | Complete source/config/migrations/docs/demo/licences/runnable packet are present and locally validated. **Final handover bookkeeping/packaging remains:** append this report verbatim, update current status/counts, commit the documentation-only result and package that exact final source beside the unchanged verified images; record its new identity/hash. This assessment authorizes no runtime changes or public deployment and requires no third review for that documented packaging step. |
| BOUND-01 | Satisfied/ongoing: completed-game personal analysis/training only; no paid calls, external provisioning or publication by this review. |

## Scores and exact pass determination

| Dimension | Weight | Score | Evidence and rationale |
| --- | ---: | ---: | --- |
| Code quality/maintainability | 25% | **8.5** | Focused safe parsing/state presentation fixes, useful boundary/pipeline tests, no architectural churn or migration/history rewrite. Original modular evidence/admission/persistence design remains sound. Dense endpoint/UI expressions and string states remain maintenance costs. |
| Chess/domain and whole feature scope | 25% | **8.7** | Required imported-game coaching loop and broader provider/practice/report scope remain real and evidence grounded; both mixed-record defects now pass exact/expanded negative and positive checks. History, mate semantics, compatible denominators and sound-alternative grading retain regression coverage and actual engine/UI evidence. |
| Ease/interface quality | 25% | **8.2** | Both full actual profiles pass without harness interruptions; mixed failures and exhausted states now explain useful next steps. Board/lesson/source/assistance and sparse coverage are coherent. Remaining nav highlight and page density prevent a higher score. |
| Reliability/security/accessibility | 15% | **8.7** | Independent full integration/security/resource/engine regression, 36 fresh default axe scans, current release identity and audited actual crash/lease/TLS/restore/fairness receipts. Supported single-host/QEMU and manual/AT limits remain explicit. |
| Verification/handover | 10% | **8.9** | Failed round preserved verbatim; exact failing boundaries now have independently passing checks and current rebuilt clean-clone/native/ARM/offline evidence. Source/archive/runtime identities, original-data preservation, licensing and benchmark limits are traceable. Prepared handover still needs this actual report recorded and final documentation-only source snapshot. |

Exact arithmetic, without rounding up:

`8.5×0.25 + 8.7×0.25 + 8.2×0.25 + 8.7×0.15 + 8.9×0.10`

`= 2.125 + 2.175 + 2.050 + 1.305 + 0.890 = 8.545/10`.

- Weighted result at least 8.0: **yes**.
- Every dimension at least 8.0: **yes** (minimum 8.2).
- Required implementation/local verification gates evidenced, including all three corrections: **yes**.
- No unresolved critical/high: **yes**, within the stated engineering assessment.
- **Round 2: PASS. Two rounds used in total; stop the review loop.** Complete the prepared local handover with this result. No public deployment is implied or authorized.

## Safe handover evidence subset

All screenshots below show synthetic fixtures only. Keep raw traces, test credentials/scripts, private backups/keys and detailed operational logs under `.local`.

- `round2/desktop-import-complete.png` — two legal records retained around one illegal record, plus duplicate reimport.
- `round2/desktop-review-coaching.png` — actual saved decision, board/notation/engine and factual coaching.
- `round2/mobile-practice-unassisted-complete.png` — actual tap-operated mobile practice and checked outcome.
- `round2/desktop-saved-progress.png` — frozen progress, partial coverage, denominators and training outcomes.
- `round2/exhausted-pgn-mobile.png` — corrected terminal recovery **labelled response fixture**; do not present it as live fault injection.
- Sanitized summary receipts: `round2/summary.json`, `journey-summary.json`, `backend-summary.json`, `terminal-axe.json`, `benchmark-audit.json`. The summary records the exact inspected/runtime/archive identities and screenshot hashes, without account emails, cookies, tokens or credentials.
