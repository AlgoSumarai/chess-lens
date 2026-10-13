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

## Engine processes

The application worker requires a database-backed `EngineSlots` coordinator. Two fixed session advisory locks bound supervised Stockfish processes across workers using that application database. A slot is acquired **before** starting the child and normally released **after** the child has exited and been reaped. Connections are dedicated, unpooled physical sessions. The local pool independently permits 1–2 processes, 1–4 threads and 16–256 MiB hash per child (defaults 2/1/64). Compose also constrains the worker to two CPUs and 1 GiB memory.

An idle child retires after five seconds. Waiting workers hold a shared demand lock; each occupied slot checks demand and connection health every half-second. A waiting worker causes the holder to retire after its current bounded search, including an idle child, with a short handoff delay before reacquisition. This retains compatible per-process caches while allowing another worker to obtain capacity. Queue waiting is cancellable and defaults to 30 seconds (`Stockfish:QueueTimeoutSeconds`, 5–120 permitted); startup remains independently bounded. A failed/cancelled process is discarded with its cache. Normal search budgets and job deadlines still apply.

Connection loss cancels active searches, and an idle disconnected child is retired by maintenance. Database command timeout is two seconds with a one-second cancellation-response allowance. This is failure detection, not instantaneous fencing: a network partition or stopped runtime can briefly leave an old child alive after PostgreSQL releases its session lock. The supported Compose deployment has one worker with the independent local process and container resource limits; the shared slots defend against additional workers while the database sessions are healthy. Multi-host partition-proof physical process limits are not claimed. Fault/restart and mixed-workload evidence must be read with that boundary.

These choices follow PostgreSQL's [session advisory-lock lifetime](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS) and Npgsql's [pooling/timeouts documentation](https://www.npgsql.org/doc/connection-string-parameters.html), checked 13 October 2026. Standalone protocol tests can construct a pool without a coordinator; the production worker always registers the coordinator from its required database connection.
