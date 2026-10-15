# ChessLens

ChessLens imports completed games, analyses them with Stockfish, and turns recurring mistakes into evidence-backed practice.

**In development.** The complete target is documented in [the build brief](docs/build-brief.md); implementation order and evidence gates are in [the plan](docs/implementation-plan.md). Do not treat this checkout as production ready until the [verification ledger](docs/verification.md) records all acceptance gates.

The stack is C# / ASP.NET Core, PostgreSQL, a separate Stockfish worker, and React / TypeScript. No paid API or LLM key is required for the core workflow. Deployment stays local until explicitly requested.

## Local startup

Use Linux Docker Engine with the Compose plugin and Python 3, on Linux or inside Ubuntu WSL 2. From the repository root:

```sh
./scripts/start.sh
```

This selects Docker Engine's `default` context, preserves an existing `.env` or generates a random database password, builds images, stops old API/worker versions, applies migrations and waits for database/worker/engine readiness. Open `http://localhost:8088`. Containers bind only to loopback. Set `CHESSLENS_DOCKER_CONTEXT` for another Linux Engine context, `CHESSLENS_PROJECT` for an isolated project, and `DATABASE_PORT`/`APP_PORT` for different local ports. The script rejects Docker Desktop. Existing volume credentials must match `.env`; restarting does not reset a volume's password. Back up before an upgrade; migration startup has a brief application interruption. Use the [backup and isolated restore commands](docs/backup-restore.md) to preserve both saved data and persistent authentication keys.

On Windows 11, install Docker Engine and the Compose plugin inside Ubuntu WSL 2, keep the checkout in its Linux filesystem, then run from PowerShell (adjust the distribution/path):

```powershell
wsl -d Ubuntu -- bash -lc 'cd ~/repos/chess-lens && ./scripts/start.sh'
```

If PowerShell's Docker CLI already connects to a Linux Engine context, `./scripts/start.ps1 -DockerContext default` performs the equivalent startup. [Microsoft's WSL commands](https://learn.microsoft.com/en-us/windows/wsl/basic-commands) and [Docker's group/session instructions](https://docs.docker.com/engine/install/linux-postinstall/) describe setup. New Docker group membership requires a fresh login or `newgrp docker`. Native Linux verification and Windows/WSL verification are tracked separately in the ledger; a documented command is not a claim that Windows has been tested.

The worker selects the checksum-pinned universal Stockfish 19 binary for Linux x86-64 or ARM64. Both architectures build and pass real UCI checks on the native Linux Engine; ARM64 execution here uses QEMU on x86 hardware. See [architecture commands and exact verification scope](docs/operations.md#architectures). Complete corresponding source, NNUE network, build recipes and licences are retained beside the executable.

If your organisation or antivirus inspects TLS and Windows can reach registries but container builds reject their certificates, the optional build-only CA path exports the public root of a certificate chain Windows already validates. It never disables certificate verification or exports private keys:

```powershell
./scripts/export-build-ca.ps1
docker compose -f compose.yaml -f compose.app.yaml -f compose.corporate-ca.yaml up -d --build
```

Use this only on a network whose interception certificate is already trusted. Ordinary networks do not need the override.

Stop containers with `docker compose -f compose.yaml -f compose.app.yaml stop`. Database and session keys persist in Docker volumes. Data deletion is available inside Settings; do not remove volumes to perform routine restarts.

## Native development

Requires .NET SDK 10.0.402 (or a newer servicing patch in that feature band), Node 24 and PostgreSQL (the database-only Compose file supplies it).

```powershell
./scripts/init-env.ps1
docker compose up -d database
. ./scripts/load-env.ps1
dotnet tool restore
dotnet restore --locked-mode
dotnet run --project src/ChessLens.Api --no-launch-profile -- --migrate
./scripts/install-stockfish.ps1
. ./scripts/load-env.ps1
```

In separate terminals, load the environment and start the API and worker:

```powershell
. ./scripts/load-env.ps1
dotnet run --project src/ChessLens.Api --no-launch-profile --urls http://127.0.0.1:5080
```

```powershell
. ./scripts/load-env.ps1
dotnet run --project src/ChessLens.Worker --no-launch-profile
```

Frontend:

```powershell
cd web
npm ci
npm run dev
```

Open `http://127.0.0.1:5173`. Vite proxies API and SignalR traffic to the API. If npm needs the existing Windows trust store, set `$env:NODE_OPTIONS='--use-system-ca'` in that terminal.

## Checks

```powershell
. ./scripts/load-env.ps1
dotnet test ChessLens.slnx
cd web
npm test
npm run build
npx playwright install chromium
npm run test:e2e
```

On native Linux/WSL, source `scripts/load-env.sh` to load the same local database settings without evaluating `.env` as shell code.

Backend integration tests create and drop only uniquely named test databases. The configured database user therefore needs local `CREATEDB` permission. Real engine tests require `Stockfish__Path`, set by `load-env.ps1` after installation. For fast rules-only checks without PostgreSQL or Stockfish, use `dotnet test --filter FullyQualifiedName~PgnTests`.

Browser tests use the native URL by default. Set `$env:CHESSLENS_URL='http://127.0.0.1:8088'` to test the container deployment instead. They create clearly named test accounts and import original sample fixtures.

## Current capabilities and limitations

Accounts, persistent PGN imports, identity selection, legal replay/variations, isolated demo workspaces, data deletion, durable mainline Stockfish review, live updates and evaluation visuals are implemented. Interactive position candidates, cancellation sequence barriers, compatible caching and snapshotted configurable classifications pass the relevant backend/component checks. Updated desktop/mobile container journeys pass with real Stockfish candidates and live position-progress frames. See the verification ledger for the exact scope of passing checks.

Core weakness snapshots now include traceable supporting positions, sample sizes, recurring/early-signal labels and clock coverage. Personal puzzles validate short tactical objectives with Stockfish, accept checked alternatives, preserve attempts and hints, and schedule spaced review. See [analytics rules](docs/analytics-rules.md) and [puzzle rules](docs/puzzle-rules.md).

Public completed-game Chess.com/Lichess imports, immutable resync and durable bounded batch reviews are now implemented. See [provider import rules](docs/provider-imports.md), [the acceptance matrix](docs/acceptance-matrix.md) and the dated live/fixture/browser evidence in the verification ledger. Grounded beginner/intermediate/advanced factual coaching, an optional bounded structured-model provider, and versioned conversion/endgame detectors are implemented with real-engine and desktop/mobile checks. See [coaching rules](docs/coaching-rules.md) and [analytics rules](docs/analytics-rules.md). Paid model requests are disabled by default; no paid live model call has been made.

This local Compose configuration uses development cookie policy for loopback HTTP. It is not a public deployment configuration. Release work must supply HTTPS, proxy trust and origin configuration, backups/restore checks, licence bundles and the full security/performance audit before publication. See [dependency choices](docs/dependencies.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [verification status](docs/verification.md).

Private uploaded PGN annotations and clocks are now stored per owner while legal game identity remains deduplicated. Upgrade first preserves legacy originals in restricted storage; older annotations whose author cannot be established stay withheld until their owner reimports their own file. Games and saved reviews remain available. Back up before this forward-only migration; see [the exact migration and deletion behavior](docs/pgn-annotation-isolation.md).

Mistake practice starts before a verified original mistake, retains your colour and source history, and saves three-decision attempts with practical Stockfish replies, independent full-strength grading, hints/undo/retry/resume and exact evidence links. See [practice rules](docs/mistake-practice.md). Weekly reports and progress now save immutable evidence revisions, local timezone boundaries, compatible decision rates with counts, missing coverage, separate rating observations and training outcomes. The durable worker backfills closed weeks with completed review activity; on-demand reports use the same capture path. See [report/progress rules](docs/reports-progress-rules.md). Integrated release and independent review gates remain required.

Resource admission uses retained rolling-day allowances and a shared pending-job ceiling; see [resource limits](docs/resource-limits.md). Data deletion does not reset consumed allowances.
