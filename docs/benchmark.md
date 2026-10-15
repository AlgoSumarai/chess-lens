# Native mixed-workload measurement — 15 October 2026

## Corrected release measurement — 3b3ae4a

A second actual run passes against clean committed application/harness source **3b3ae4a808cc2dda5e1ce789ccab2da8558cbf86**, using the corrected native images. The source clock/recovery fixes do not change the engine, quotas, scheduler, search profiles or leases. The same original artificial workload completes **six reviews / 182 saved half-moves**, three quick/three deep, **six new imports / 24 half-moves**, and all five side-job lanes. No imports fail or duplicate. Both synthetic libraries are empty after cleanup. Whole measured workload time is **222.009 seconds**; the **23.938-second** preparation and subsequent queue setup are excluded.

The i5-10210U/eight-logical-CPU host and two-process/one-thread/64 MiB-hash engine settings are unchanged from the detailed scope below. Reported memory is 7,336,696 KiB (the earlier receipt reports 7,336,688). The active desktop/idle validation services and normal health probes remain present; baseline one-minute load is **5.99**. No builds, other test suites, emulated application or archive compression run during this measurement. This is another finite observation on an active desktop, not a controlled before/after speed experiment; the earlier 146.169-second result remains historical, without attributing the elapsed difference to the parser fix.

Practice initialization is observed active by **10.404 s**, position complete by **12.101 s**, insight by **21.516 s**, puzzle ready by **35.250 s** and report complete by **36.543 s**. Every side-job observation has both import/review backlog; the later puzzle/report observations retain **four imports and five reviews**. Quick recorded engine work totals **36.383 s**, observed complete by **64.922 s**; deep work totals **150.069 s**, observed by **222.005 s**. The final import is observed by **169.826 s**. Queue order/profile rechecks/observation overhead keep these distinct from isolated engine speed.

| Service | Sampled peak CPU (100% = one logical CPU) | Sampled peak working memory |
| --- | ---: | ---: |
| api | 115.69% | 149.10 MiB |
| worker | 204.38% | 412.50 MiB |
| database | 89.86% | 55.88 MiB |
| frontend | 0.66% | 10.57 MiB |

There are **110 samples**, mean interval **2.024 s**. Concurrent aggregate peaks are **317.47% CPU / 604.39 MiB working memory**. At most **two Stockfish processes** are observed; the largest recorded per-search process peak is **332,382,208 bytes (316.98 MiB)**. Working-memory/cache, sampling, noncoincident per-service peaks and accounting-window limitations remain those defined below; the worker's configured two-CPU/1-GiB cap is unchanged. Compatible cache stays enabled; **eight** final saved best/played evidence entries have `CacheHit=true`, not a count of every search call.

Evidence: `.local/evidence/review/fixes/benchmark-run/benchmark.json`, `benchmark-run.log`, `benchmark-summary.json`, `benchmark-stop.log` and the corrected startup receipt. The first repeat startup used the repository `.env` against the older benchmark volume, whose original credentials came from `.local/clean-checkout-bee92d0/.env`. PostgreSQL correctly rejected authentication before migration; `benchmark-start.log` retains exit 139 and SQLSTATE 28P01. The original credential file was privately verified without emitting or changing its password, then `benchmark-start-correct-env.log` passes with explicit `--env-file`. That setup failure is not a passing application/reliability observation. Existing data/credentials are preserved.

## Earlier measurement — bee92d0

One opt-in run of `scripts/benchmark.py` passes against the actual patched
Compose application (`bee92d0` runtime inputs). The harness is `3fc2b0c` plus
the corrected six-import fixture; the receipt explicitly records a dirty
worktree. The first preparation attempted six active imports per owner and
correctly received HTTP 429 at the existing three-active-import limit. Its
failure and cleanup remain in `run1`; no application allowance was raised.

Hardware/environment: Intel Core i5-10210U, four physical cores/eight logical
CPUs, 7,336,688 KiB reported host memory (about 7.00 GiB), Ubuntu 26.04.1,
Linux 7.0.0-38, native Docker Engine 29.9.0. The desktop remains active; host
one-minute load at baseline was 2.00. No builds, other test suites, emulated
application or archive compression ran during measurement. Other native idle
validation services and normal health probes remained present. Three baseline
container samples are retained, including a periodic worker health-search
startup spike; this is not an otherwise idle laboratory host.

Stockfish 19 uses two maximum supervised processes, one thread/64 MiB hash
each. Worker limits are two CPUs/1 GiB; API memory limit is 512 MiB. Actual
per-decision settings, best/restricted searches and verification budgets are
exported in the receipt. Quick starts at 30,000 nodes/1,000 ms; deep starts at
250,000 nodes/5,000 ms. Flagged/bounded decisions are rechecked at stronger
budgets, including 250,000/500,000-node searches. These are bounds, not a
promise that every search consumes its maximum.

Preparation (16.401 seconds) imports original artificial fixtures and completes
one six-ply deep review to supply verified training evidence. It is excluded
from workload timing. The worker is stopped only while preparing queues, then
started with six reviews, six PGN deliveries, and one insight, puzzle,
practice-initialization, progress-report and interactive-position job. Two
owners each respect the three-active-review/import limits. No lease/timestamp,
deadline or production scheduler setting is changed.

| Work | Games / half-moves | Recorded engine work | Completion observed by |
| --- | --- | --- | --- |
| Quick reviews | 3 / 91 | 21.963 s total | 37.691 s |
| Deep reviews | 3 / 91 | 102.184 s total | 146.163 s |
| PGN deliveries | 6 / 24 | Not engine work | 114.094 s |

All six reviews complete **182/182 saved half-moves**, and all six imports
deliver one new game with zero failures/duplicates. Whole measured workload
time is **146.169 seconds**, including worker start, queue waiting, side jobs
and observation overhead. Quick work was queued before deep work; the table
is not a controlled head-to-head profile speed comparison.

Practice reaches active state by **9.388 s**, interactive position completion
by **10.326 s**, insight by **19.427 s**, puzzle readiness by **24.603 s** and
report completion by **25.579 s**. Each observation records both mainline and
import backlog still pending: the later puzzle/report observations have five
reviews and four imports pending. HTTP checks occur about every four seconds,
so these are observed end-to-end upper bounds, not isolated search latency.
They demonstrate queue progress under this finite mixed workload, not a fixed
latency guarantee for arbitrary job sizes or service failures.

| Service | Sampled peak CPU (100% = one logical CPU) | Sampled peak working memory |
| --- | ---: | ---: |
| API | 117.52% | 137.50 MiB |
| Worker, including engine children | 200.92% | 413.80 MiB |
| PostgreSQL | 74.16% | 51.34 MiB |
| Frontend | 0.40% | 8.48 MiB |

There are **73 samples**, mean interval **2.015 seconds**. Aggregate concurrent
peaks are **304.56% CPU** and **601.22 MiB working memory**; independent service
peaks do not necessarily coincide. Docker Linux working usage excludes
reclaimable cache. The largest separately recorded per-search process memory
peak is **332,914,688 bytes (317.49 MiB)**; hash allocation alone is not engine
resident memory. At most **two Stockfish processes** are observed. Sampling
can miss shorter spikes, and CPU accounting windows can slightly straddle the
two-CPU worker quota interval.

Compatible per-process cache stays enabled; histories share opening positions.
Four stored final best/played search entries have `CacheHit=true`; they are
evidence entries, not a count of all executed searches or unique cache calls.
Preparation caches are lost with the worker stop; later within-run reuse is
normal. No uncached throughput, peak production capacity, native ARM speed or
universal device performance is inferred. Browser-suite durations are separate
functional evidence.

Evidence: `.local/evidence/benchmark/{start.log,images.yaml,run1.log,run1/,run2.log,run2/benchmark.json}`.
The passing receipt includes exact image IDs, raw hardware details, every
sample/event, pending-queue counts and all 182 saved decision/search budgets.
Both synthetic owners' imported data is deleted afterward and their empty
libraries verified; retained account/budget metadata remains by design.
Reproduce alone on an explicitly named `chesslens-*-validation` project:

```sh
python3 scripts/benchmark.py --project chesslens-benchmark-validation \
  --url http://localhost:8094 --output .local/evidence/benchmark/new-run
```
