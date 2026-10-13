# Resource admission and retained usage

Every API/worker producer reserves work inside its existing transaction, under the owner's advisory lock followed by the shared resource-admission lock. Reservations commit with the new job. Rejected validation or rolled-back creation consumes no allowance. Concurrent requests from different owners share a ceiling of **1,000 pending durable jobs** across imports, batch controllers, waiting/queued/running game reviews, positions, insights, puzzles, coaching, practice jobs and reports. A batch controller and each unfinished child count separately. Engine/provider/model execution have additional bounds described in their contracts; this queue limit alone does not establish execution concurrency.

Rolling-day allowances use `ResourceUsages`, retained when imported/training data is deleted. Cancellation, failure and deletion do not refund already reserved work. A compatible existing request returned unchanged makes no new reservation. Each owner has these allowances:

| Work | Units in the preceding 24 hours |
| --- | ---: |
| PGN, public-provider or demo import batch | 20 |
| Game review | 5,000 selected half-moves |
| Interactive position review | 200 |
| New position review channel | 200 |
| Weakness snapshot | 20 |
| Puzzle validation | 20 |
| New puzzle attempt | 200 |
| Saved coaching explanation | 300 |
| New mistake-practice session | 40 |
| Mistake-practice engine job | 200 |
| Report/progress snapshot, including scheduler requests | 20 |

Existing per-owner active-work limits apply as well. HTTP 429 explains whether the owner allowance or shared queue is full. Cancellation remains available at capacity. A position request that supersedes work in its own channel consumes another daily unit but only its net additional queue slots. Pure metadata operations and factual explanations consume their relevant daily allowance without claiming a nonexistent pending job. Demo registration and admission are atomic: a full queue cannot leave a newly created demo account behind.

A direct game-review request reuses only the same active profile and current analysis algorithm; a conflicting depth returns 409. An imported batch requesting deep analysis does not reuse a quick run. Its reserved deep run waits until another active review of that game finishes. Completed compatible-profile runs reused by a batch retain their actual engine/settings evidence; reuse does not imply the current worker has those settings.

`RetainedResourceBudgets` backfills available prior-day job rows once. Historical work already deleted before this upgrade cannot be reconstructed. Practice/report reservations already used retained accounting and are not backfilled again. The migration is forward-only to prevent accidental replay or removal of accounting. Stop old API/worker versions before migrating; restore a pre-upgrade backup or use a forward fix rather than downgrade. The ledger contains owner ID, work kind, units and timestamp, never PGN content or moves.

Verification is recorded in [the ledger](verification.md). Queue tests use explicitly labelled administrative pending-job fixtures; they are not throughput, process-crash or provider-network evidence.
