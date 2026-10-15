#!/usr/bin/env bash
# Same locked/native-Engine checks in CI and an isolated local checkout.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export CHESSLENS_PROJECT="${CHESSLENS_PROJECT:-chesslens-ci-validation}"
if [[ ! "$CHESSLENS_PROJECT" =~ ^chesslens-([a-z0-9-]+-)?validation$ ]]; then
  echo 'CI service faults require an isolated chesslens-*-validation project.' >&2
  exit 1
fi
export DOCKER_CONTEXT=default
export DATABASE_PORT="${DATABASE_PORT:-54349}"
export APP_PORT="${APP_PORT:-8092}"
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export Coaching__Enabled=false Coaching__ApiKey=''
export Coaching__DailyOwnerMicroDollars=0 Coaching__DailyGlobalMicroDollars=0
chesslens_ci_directory=".local/ci"
mkdir -p "$chesslens_ci_directory"
chesslens_ci_compose=(docker --context default compose -p "$CHESSLENS_PROJECT" -f compose.yaml -f compose.app.yaml)
cleanup() {
  "${chesslens_ci_compose[@]}" ps --format json > "$chesslens_ci_directory/services.json" 2>/dev/null || true
  "${chesslens_ci_compose[@]}" logs --no-color --tail 500 api worker frontend > "$chesslens_ci_directory/services.log" 2>&1 || true
  if [[ "${CHESSLENS_KEEP_RUNNING:-0}" != 1 ]]; then
    "${chesslens_ci_compose[@]}" stop > "$chesslens_ci_directory/stop.log" 2>&1 || true
  fi
}
trap cleanup EXIT
run_step() {
  local chesslens_ci_step="$1"; shift
  printf 'Checking %s\n' "$chesslens_ci_step"
  if ! "$@" > "$chesslens_ci_directory/$chesslens_ci_step.log" 2>&1; then
    echo "Failed: $chesslens_ci_step; see $chesslens_ci_directory/$chesslens_ci_step.log" >&2
    return 1
  fi
}
git rev-parse HEAD > "$chesslens_ci_directory/commit.txt"
dotnet --info > "$chesslens_ci_directory/dotnet.txt"
node --version > "$chesslens_ci_directory/node.txt"
docker --context default version --format '{{.Server.Version}}' > "$chesslens_ci_directory/docker.txt"
run_step compose-start bash scripts/start.sh
# Read the freshly generated password as data; never put it in command arguments.
. scripts/load-env.sh
export Stockfish__Path="$PWD/.local/stockfish-19/stockfish/stockfish"
if [[ ! -x "$Stockfish__Path" ]]; then
  run_step stockfish-install bash scripts/install-stockfish.sh
fi
run_step stockfish-materials python3 scripts/check-stockfish-materials.py \
  --directory .local/stockfish-19/stockfish --architecture "$(docker --context default image inspect --format '{{.Architecture}}' "$CHESSLENS_PROJECT-worker")" \
  --output "$chesslens_ci_directory/stockfish-materials.json"
run_step locked-restore dotnet restore --locked-mode
run_step tools dotnet tool restore
run_step npm-ci npm --prefix web ci --no-fund
run_step licences python3 scripts/licenses.py
run_step backend dotnet test ChessLens.slnx --no-restore --logger 'trx;LogFileName=backend.trx' --results-directory "$chesslens_ci_directory/test-results"
run_step components npm --prefix web test
run_step frontend npm --prefix web run build
run_step npm-audit npm --prefix web audit --audit-level=low
dotnet list ChessLens.slnx package --vulnerable --include-transitive --format json --no-restore \
  > "$chesslens_ci_directory/nuget-audit.json" 2> "$chesslens_ci_directory/nuget-audit.stderr"
python3 - <<'AUDIT'
import json
from pathlib import Path
data = json.loads(Path('.local/ci/nuget-audit.json').read_text())
if len(data.get('projects', [])) != 4:
    raise SystemExit('NuGet audit did not report all four projects.')
flagged = [p for project in data['projects'] for framework in project.get('frameworks', [])
           for key in ('topLevelPackages', 'transitivePackages') for p in framework.get(key, []) if p.get('vulnerabilities')]
if flagged:
    raise SystemExit('NuGet audit reports vulnerable packages; inspect the saved JSON.')
print('The dated NuGet audit reports no flagged packages in all four projects.')
AUDIT
if [[ "${CHESSLENS_BROWSER_READY:-0}" != 1 ]]; then
  run_step browsers bash -c 'cd web && npx playwright install --with-deps chromium'
fi
export CHESSLENS_URL="http://localhost:$APP_PORT"
export CHESSLENS_TEST_DATABASE_CONTAINER="$CHESSLENS_PROJECT-database-1"
export CHESSLENS_FAULT_PROJECT="$CHESSLENS_PROJECT"
run_step browsers-connected npm --prefix web run test:e2e
run_step image-source-uci python3 scripts/check-engine-image.py --image "$CHESSLENS_PROJECT-worker" \
  --platform "linux/$(docker --context default image inspect --format '{{.Architecture}}' "$CHESSLENS_PROJECT-worker")" \
  --verify-source --output "$chesslens_ci_directory/image-source-uci.json"
echo "Checks passed; evidence is in $chesslens_ci_directory and web/playwright-report."
