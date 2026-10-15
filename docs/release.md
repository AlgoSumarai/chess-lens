# Offline local release

Release preparation stays on native Linux Docker Engine. Nothing here publishes
to a registry, provisions infrastructure or deploys publicly. The verification
ledger and independent review determine readiness; creating an archive alone
does not pass those gates.

The accepted handover on this host is `.local/releases/2026.10.15-reviewed/`,
with image version `2026.10.15-r2`. Its manifest identifies the final committed
source and all artifact hashes. See [the handover](handover.md) and
[independent Round 2 result](quality-review.md) for acceptance and platform limits.

`scripts/release.py package` takes an explicit JSON mapping of `linux/amd64` and
`linux/arm64`, each containing `api`, `worker`, `frontend` and `database` image
references. Build and verify those images from the stated application commit
first. Supply the exact notice directories produced by
`scripts/collect-image-notices.py` for each platform. Use Python 3.12 or newer.
The packager requires a clean committed source tree and rejects changed runtime
inputs relative to the stated application commit.

```sh
python3 scripts/release.py package \
  --images .local/release-images.json --application-commit APPLICATION_COMMIT \
  --notices-amd64 .local/release-notices-amd64 \
  --notices-arm64 .local/release-notices-arm64 \
  --version 2026.10.15-r2 --output .local/releases/2026.10.15-reviewed
```

The new directory contains committed source, a compressed image archive and
Compose override per architecture, exact OS/runtime notice exports, a release
manifest and SHA-256 receipts. Complete application licence texts are in source
and API/worker images; browser texts are in the frontend. Exact Stockfish
binary/source/NNUE/build/licence materials are inside each worker image.
`release.json` records source and application commits, image config IDs,
architecture, sizes and available registry/BuildKit manifest digests. The source
commit may additionally contain verification/documentation tools; changed
runtime inputs cannot silently reuse an earlier image set.

No running database, credentials, key volume, TLS private key, `.env` or private
PGN backup is packaged. Use the separate [backup/restore workflow](backup-restore.md)
for application data. The manifest hashes identify the observed artifacts;
they are integrity receipts, not a public signed supply-chain attestation or
claim of byte-identical compiler output on another host.

For a fresh offline start, select the host architecture (`amd64` or `arm64`):

```sh
cd /absolute/path/to/release
sha256sum -c SHA256SUMS
docker --context default image load -i images-amd64.tar.gz
mkdir checkout
tar -xzf source.tar.gz -C checkout
cd checkout
python3 - <<'PY'
from pathlib import Path
import os, secrets
try:
    descriptor = os.open('.env', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, 'w') as stream:
        stream.write('POSTGRES_PASSWORD=' + secrets.token_hex(32) + '\n')
except FileExistsError:
    pass
PY
python3 scripts/release.py verify .. --platform linux/amd64
CHESSLENS_PROJECT=chesslens-release-validation APP_PORT=8088 DATABASE_PORT=54349 \
  docker --context default compose -p chesslens-release-validation \
  -f compose.yaml -f compose.app.yaml -f ../compose-amd64.yaml \
  up -d --no-build --pull never --wait --wait-timeout 240
```

Use unused ports/project names for a new validation installation. The command
does not remove volumes or replace an existing password. Open
`http://localhost:8088`; check `/health/ready`. The digest receipts and verifier
check that loaded local tags still resolve to the selected config IDs. Image
loading does not rerun a build or download packages. For ARM64 use both ARM64
archive/override names; architecture health grace under emulation is documented
in [operations](operations.md#architectures), not a native performance promise.

An existing installation needs its original `.env` and database/key volumes.
Back it up, stop old API and worker before migrations, select the same original
project and verified volumes, then start the new image set. Prefer a restored
copy first, following the role-aware [cutover instructions](deployment.md).
Never initialize new credentials against an existing database volume.
Production-oriented HTTPS uses `compose.production.yaml` plus the release image
override, external role secrets and TLS files; the override supplies images and
platform only and keeps those security settings.

Run the [demonstration](demo.md) after startup. The clean-checkout checks and
exact architecture/release/migration evidence are listed in
[verification](verification.md); Windows/WSL commands are documented separately
from the actual Linux/QEMU checks.
