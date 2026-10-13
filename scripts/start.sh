#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
chesslens_context="${CHESSLENS_DOCKER_CONTEXT:-default}"
chesslens_project="${CHESSLENS_PROJECT:-chesslens}"
chesslens_platform="$(docker --context "$chesslens_context" info --format '{{.OSType}} {{.OperatingSystem}}')"
if [[ "$chesslens_platform" != linux\ * || "$chesslens_platform" == *"Docker Desktop"* ]]; then
  echo 'Select a Linux Docker Engine context with CHESSLENS_DOCKER_CONTEXT.' >&2
  exit 1
fi
umask 077
python3 - <<'PY'
from pathlib import Path
import secrets
p = Path('.env')
try:
    with p.open('x') as f:
        f.write('POSTGRES_PASSWORD=' + secrets.token_hex(32) + '\n')
    print('Created local .env with a random database password.')
except FileExistsError:
    pass
values = [s.split('=', 1)[1].strip() for s in p.read_text().splitlines() if s.startswith('POSTGRES_PASSWORD=')]
if len(values) != 1 or not values[0] or values[0] == 'replace-with-a-long-random-password':
    raise SystemExit('Set one nonempty POSTGRES_PASSWORD in .env before startup; existing credentials are never replaced.')
PY
chesslens_compose=(docker --context "$chesslens_context" compose -p "$chesslens_project" -f compose.yaml -f compose.app.yaml)
"${chesslens_compose[@]}" build
# Old code must stop before forward-only privacy/accounting migrations run.
"${chesslens_compose[@]}" stop api worker
"${chesslens_compose[@]}" up -d --no-build --wait --wait-timeout 240
echo "ChessLens local URL: http://localhost:${APP_PORT:-8088}"
echo "Readiness: http://localhost:${APP_PORT:-8088}/health/ready"
