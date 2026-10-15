# Reproducible application checks

`.github/workflows/checks.yaml` runs the same `scripts/ci-check.sh` used for
local release verification. It uses a native Ubuntu 24.04 Engine runner,
read-only repository permissions and commit-pinned official checkout/.NET/Node/
artifact actions. Their upstream release refs were checked on 15 October 2026.
The SDK comes from `global.json`, Node is 24.22.0, and both dependency graphs
are resolved from committed locks. No model credential is provided; paid
coaching is explicitly disabled. The workflow does not publish images or deploy.

The script builds and starts a separate development-mode validation stack with
new credentials when `.env` does not exist, applies migrations, installs the
checksum-pinned complete Stockfish distribution, checks source materials, runs
locked restore/tool restore, backend/component/browser checks and the frontend
build, checks exact licence inventories and both security feeds, and exercises
the worker image's source hashes/real UCI. It also exports and verifies every
installed OS package's copyright text, common licences and available .NET runtime
notices from the actual four runtime images into `.local/ci/image-notices`.
These exports run offline without application volumes or capabilities. Browser tests include desktop/mobile
and the opt-in actual API outage: the project name and Compose labels are
validated before faults. The development database permits the uniquely named
integration-test databases; production runtime-role restrictions have separate
HTTPS tests and evidence.

For a local run, use a separate clean checkout, Docker Engine and the documented
.NET/Node/Python prerequisites:

```sh
CHESSLENS_PROJECT=chesslens-ci-validation APP_PORT=8092 DATABASE_PORT=54349 \
  bash scripts/ci-check.sh
```

Use an unused validation project and ports. The script preserves existing `.env`
credentials and never removes volumes. By default it stops its selected stack
on exit, including failures. Set `CHESSLENS_KEEP_RUNNING=1` to retain that local
validation application. It never targets a project outside the explicit
`chesslens-*-validation` naming boundary. It does not stop other projects.

Playwright installation includes Chromium/system dependencies on the supported
CI runner. A local environment that already has the verified matching browser
may set `CHESSLENS_BROWSER_READY=1` and its `PLAYWRIGHT_BROWSERS_PATH`; this skips
only installation, not browser tests. Record that distinction with local
evidence. `CHESSLENS_EMULATED_ARCH=arm64` is an explicit separate compatibility
mode, not the native CI performance setting.

Evidence stays in `.local/ci` and the scoped Playwright report/results folders.
The artifact step includes those exact hidden paths, not `.env`, TLS/private
keys, backups or the rest of `.local`. They contain only the fresh synthetic
test accounts/games and test traces in the CI run. Generated TypeScript build
state is ignored instead of producing tracked changes after each build.

The workflow is prepared locally. Do not describe it as remotely passing before
the GitHub run actually exists; local execution of its script verifies the
commands, while hosted action execution remains a distinct observation. No
remote workflow, paid infrastructure or public deployment is started here.
