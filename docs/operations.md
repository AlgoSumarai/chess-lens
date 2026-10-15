# Local operations and readiness

Use **Linux Docker Engine**, including Engine installed inside Ubuntu WSL 2 for Windows 11. `scripts/start.sh` and `scripts/start.ps1` explicitly select an Engine context (default `default`), reject Desktop, preserve credentials, build before stopping old application versions, apply migrations, then wait for healthy services. The default Compose deployment runs one API, one worker and one PostgreSQL database. Keep the same project name and persistent volumes for ordinary restarts. A different Engine daemon has different images/volumes; do not assume Desktop data has moved merely because a project name matches.

When upgrading, keep a recoverable database/key backup. Build first; stop old API and worker before applying forward-only privacy/accounting migrations; start matching new versions afterward. `up --build` with old writers left running is not the supported upgrade procedure. Data/credentials in a pre-existing volume are never reset by startup scripts. [PGN preservation](pgn-annotation-isolation.md) and [quota migration](resource-limits.md) describe the precise historical limits.

The local password-authenticated Compose database does not use Kerberos. Its connection strings explicitly set `GSS Encryption Mode=Disable`, following [Npgsql's documented handling of missing GSS libraries](https://www.npgsql.org/doc/security.html#gss-session-encryption-gss-api), checked 13 October 2026. This avoids a harmless native-library warning in minimal runtime images; it does not change SSL/TLS settings. Production database TLS configuration remains part of the separate production example gate.

## Health

`GET /health/live` reports only API process liveness. `GET /health/ready` requires a reachable, migrated database and at least one ready worker heartbeat from the preceding 15 seconds. It returns HTTP 503 with a service category when unavailable; it does not reveal connection strings or owner data. Database checks have a three-second deadline.

The worker stores only a random instance ID, last heartbeat, engine-check time, lane progress times and readiness. Every five seconds it persists a heartbeat and atomically writes a local health file after database success. Rows older than one day are pruned. No account/game content enters this table or file. A restart removes the prior health file before probing. `dotnet ChessLens.Worker.dll --healthcheck` checks that its local worker PID is alive and its successful heartbeat is fresh. Each worker container has its own temporary filesystem.

Successful supervised engine searches refresh engine health; if no successful search occurred in 30 seconds, a small 1,000-node protocol probe uses the same bounded global/local pool. Probes have a 150-second outer deadline; recent failure makes the next heartbeat unready until a successful search. An engine check older than three minutes also fails readiness. Main/interactive loops must have progressed within 16/four minutes. Their outer processing deadlines are 15/three minutes respectively, in addition to each job's smaller engine, provider, lease and grading limits. A busy bounded game review is allowed to finish while interactive work uses the separate lane.

Compose healthchecks run every ten seconds, with a five-second command timeout, a three-minute cold-start grace and three failures before Docker reports unhealthy. The frontend starts after API readiness succeeds. This detects application dependencies; it does not automatically restart an unhealthy process. Use logs and saved job state to identify a failure. API liveness remains available during a worker outage; readiness can take up to the heartbeat freshness window to reflect a stopped worker.

The frontend healthcheck also calls readiness through nginx, so a healthy API alone cannot hide a broken proxy. nginx uses Docker's internal resolver with a five-second cache and a shared dynamic upstream. This follows [nginx's `resolve` contract](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#server) and [Compose's changing-container-address behavior](https://docs.docker.com/compose/how-tos/networking/). `scripts/check-proxy-recreation.py` forces an API address change in an isolated validation project and checks recovery without restarting the frontend. Its temporary address-holder container is removed afterward; named volumes are preserved.

The local API's exact SignalR origin allowlist follows `APP_PORT`, for both
`localhost` and `127.0.0.1`. A custom port must not silently disable notifications.
Origin checks remain exact; unrelated origins are rejected.

## Architectures

The Dockerfile automatically selects the pinned **Linux x86-64 or ARM64**
Stockfish 19 universal archive using the image target architecture. Native ARM
hosts need no overlay. Both engine archives retain the original sources and
notices plus supplemental complete source, build workflows and the exact NNUE
network. The stable worker executable is `/opt/stockfish/stockfish`.

The SDK and Node build stages run on `BUILDPLATFORM`. The backend publishes
portable managed DLLs with `UseAppHost=false`, following [Microsoft's portable
publishing contract](https://learn.microsoft.com/en-us/dotnet/core/deploying/#portable-binaries);
the frontend produces portable browser JavaScript/CSS. Final .NET/nginx images
and Stockfish use the target architecture. This keeps cross-architecture builds
from spending time emulating their compiler/bundler.

For host development, `scripts/install-stockfish.sh` selects `uname -m`, installs
into a new directory and refuses an existing destination. The Windows x86-64
installer has the same source/network packaging and no-overwrite policy; its
latest changes have not been executed on Windows in this Linux session.
See [licence and exact build materials](../licenses/stockfish/BUILD.md).

Cross-architecture compatibility checks can use `compose.arm64.yaml` **after**
the operator has enabled the documented QEMU ARM64 handler on their native Linux
Engine. The application startup script does not install privileged emulators.
Use a separate project and new volumes/ports:

```sh
APP_PORT=8089 DATABASE_PORT=54340 docker --context default compose \
  -p chesslens-arm64-validation -f compose.yaml -f compose.app.yaml \
  -f compose.arm64.yaml build
APP_PORT=8089 DATABASE_PORT=54340 docker --context default compose \
  -p chesslens-arm64-validation -f compose.yaml -f compose.app.yaml \
  -f compose.arm64.yaml up -d --no-build --wait --wait-timeout 900
CHESSLENS_URL=http://localhost:8089 CHESSLENS_EMULATED_ARCH=arm64 \
  CHESSLENS_TEST_DATABASE_CONTAINER=chesslens-arm64-validation-database-1 \
  npm --prefix web run test:e2e -- reports.spec.ts
```

The emulation overlay gives API/worker/frontend health commands a 20-second
timeout and a ten-minute startup grace. Ordinary native health limits stay
unchanged. The explicit browser flag scales test waits sixfold; **production
job/engine deadlines are unchanged**. Record this flag with results. QEMU
execution establishes architecture compatibility, not native ARM throughput.
The verification ledger preserves initial cold-start/registration failures and
the exact scope of subsequent runs. Windows/WSL and native ARM hardware checks
must not be inferred from these Linux/QEMU runs.

## Logs and recovery evidence

API and worker console output is structured JSON. HTTP responses carry `X-Correlation-ID`; API log scopes and error responses carry the corresponding correlation identifier. Processing slices have `JobKind`, `JobId`, a new `ExecutionId`, elapsed milliseconds and sanitized exception type. Engine start/retirement logs include child PID and thread/hash settings. A finished processing slice means that invocation returned, not that the durable job succeeded; query its persisted state to distinguish completion, retry, cancellation or rejection.

Logs deliberately omit raw PGNs, annotations, usernames, model requests/responses, exception messages from external sources and credentials. Stack traces are retained for unexpected outer/API errors without their exception message. EF sensitive-data logging remains disabled. Operational logs and backups remain private local artifacts.

Session interruption and engine-child OS termination checks are recorded in [verification](verification.md). Native whole-worker termination/restart, database-server outage and actual API-outage/missed-notification recovery now have dated evidence. [Backup/restore commands](backup-restore.md) preserve both the database and Data Protection keys and verify all restored table/key contents. Exhausted leases, repeated delivery under faults, mixed-workload measurements, production HTTPS and release image checks remain open until their dated evidence is recorded. A readiness implementation alone does not close those gates.
