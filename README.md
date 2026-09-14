# ChessLens

ChessLens imports completed games, analyses them with Stockfish, and turns recurring mistakes into evidence-backed practice.

**In development.** The complete target is documented in [the build brief](docs/build-brief.md); implementation order and evidence gates are in [the plan](docs/implementation-plan.md). Do not treat this checkout as production ready until the [verification ledger](docs/verification.md) records all acceptance gates.

The stack is C# / ASP.NET Core, PostgreSQL, a separate Stockfish worker, and React / TypeScript. No paid API or LLM key is required for the core workflow. Deployment stays local until explicitly requested.

## Local startup

On Windows 11 with Docker running, execute from the repository root:

```powershell
./scripts/start.ps1
```

This generates a random local database password, builds the images, applies versioned migrations and starts the database, API, worker and frontend. Open `http://localhost:8088`. Containers bind only to loopback. No cloud service is created.

On Linux or Ubuntu WSL with Docker Engine and the Compose plugin, create `.env` from `.env.example` with a random password, then:

```sh
docker compose -f compose.yaml -f compose.app.yaml up -d --build
```

Current worker image targets Linux x86-64 and bundles the universal Stockfish 19 binary. ARM64 packaging remains a release task.

If your organisation or antivirus inspects TLS and Windows can reach registries but container builds reject their certificates, the optional build-only CA path exports the public root of a certificate chain Windows already validates. It never disables certificate verification or exports private keys:

```powershell
./scripts/export-build-ca.ps1
docker compose -f compose.yaml -f compose.app.yaml -f compose.corporate-ca.yaml up -d --build
```

Use this only on a network whose interception certificate is already trusted. Ordinary networks do not need the override.

Stop containers with `docker compose -f compose.yaml -f compose.app.yaml stop`. Database and session keys persist in Docker volumes. Data deletion is available inside Settings; do not remove volumes to perform routine restarts.

## Native development

Requires .NET 10 SDK, Node 24 and PostgreSQL (the database-only Compose file supplies it).

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

Backend integration tests create and drop only uniquely named test databases. The configured database user therefore needs local `CREATEDB` permission. Real engine tests require `Stockfish__Path`, set by `load-env.ps1` after installation. For fast rules-only checks without PostgreSQL or Stockfish, use `dotnet test --filter FullyQualifiedName~PgnTests`.

Browser tests use the native URL by default. Set `$env:CHESSLENS_URL='http://127.0.0.1:8088'` to test the container deployment instead. They create clearly named test accounts and import original sample fixtures.

## Current capabilities and limitations

Accounts, persistent PGN imports, identity selection, legal replay/variations, isolated demo workspaces, data deletion, durable mainline Stockfish review, live updates and evaluation visuals are implemented. Interactive position candidates, cancellation sequence barriers, compatible caching and snapshotted configurable classifications pass the relevant backend/component checks. Updated desktop/mobile container journeys pass with real Stockfish candidates and live position-progress frames. See the verification ledger for the exact scope of passing checks.

Core weakness snapshots now include traceable supporting positions, sample sizes, recurring/early-signal labels and clock coverage. Personal puzzles validate short tactical objectives with Stockfish, accept checked alternatives, preserve attempts and hints, and schedule spaced review. See [analytics rules](docs/analytics-rules.md) and [puzzle rules](docs/puzzle-rules.md).

Later milestones remain required: provider imports, grounded rating-aware optional model explanations, conversion/endgame detectors, mistake practice, weekly reports and comparable progress. Final release checks remain outstanding.

This local Compose configuration uses development cookie policy for loopback HTTP. It is not a public deployment configuration. Release work must supply HTTPS, proxy trust and origin configuration, backups/restore checks, licence bundles and the full security/performance audit before publication. See [dependency choices](docs/dependencies.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [verification status](docs/verification.md).
