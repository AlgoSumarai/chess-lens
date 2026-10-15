# Local handover — 15 October 2026

**Review status:** Round 1 failed three medium-severity import/recovery gates; their corrections pass the complete engineering checks, and independent Round 2 remains required. This is a prepared local candidate handover, with final readiness pending that assessment.

The running application is http://localhost:8088. It uses native Linux Docker Engine's `default` context, project `chesslens-engine-validation`, database port 54339 and the original local `.env`, database and authentication-key volumes. Open the application, create/sign in to your account and follow [the demonstration](demo.md). No paid model key is required.

## Preserve the existing installation

This host's validation project name differs from the generic fresh-install default. Keep that project name and its volumes when restarting. Run from the repository root with Docker group access (a fresh login may be needed):

```sh
DATABASE_PORT=54339 APP_PORT=8088 docker --context default compose \
  -p chesslens-engine-validation -f compose.yaml -f compose.app.yaml \
  -f .local/releases/2026.10.15-r2/compose-amd64.yaml stop api worker
DATABASE_PORT=54339 APP_PORT=8088 docker --context default compose \
  -p chesslens-engine-validation -f compose.yaml -f compose.app.yaml \
  -f .local/releases/2026.10.15-r2/compose-amd64.yaml \
  up -d --no-build --pull never --wait --wait-timeout 240
```

The command uses the already loaded release images. It preserves the existing password and storage. Back up before an upgrade; [backup/restore](backup-restore.md) and [HTTPS/copy cutover](deployment.md) cover data and persistent keys. [Offline startup](release.md) covers a separate fresh installation with unused ports. The local HTTPS example at https://localhost:8443 uses a private test CA and a restored database; it is separate from the normal HTTP app.

## Release and evidence

The corrected candidate packet is `.local/releases/2026.10.15-r2/`: committed source, x86-64/ARM64 image archives, Compose overrides, full runtime notices and `release.json`/`SHA256SUMS`. Runtime application inputs are `3b3ae4a808cc2dda5e1ce789ccab2da8558cbf86`; the source manifest records the later documentation/test/review snapshot. The packager rejects any changed runtime inputs when reusing those images.

Use [the acceptance matrix](acceptance-matrix.md), [verification ledger](verification.md), [independent review](quality-review.md), [release record](release-record.md) and [benchmark](benchmark.md) for checks and their limits. Raw logs/TRX/browser reports/traces/screenshots and receipts remain in the named `.local/evidence/` paths; clean-checkout artifacts also remain in `.local/clean-checkout-bee92d0/.local/ci/` and its `web/` reports. Private database dumps, credentials and TLS/key material are excluded from release archives.

The delivered scope includes both public provider imports/resync, grounded no-key/optional-model coaching, conversion/endgame findings, saved mistake practice, immutable weekly reports/comparable progress, connected desktop/mobile screens, ownership/resource/recovery controls and operational release tooling. Licence texts, board artwork provenance and complete exact Stockfish binary/source/network/build materials accompany the distributions.

## Validation limits

Linux x86-64 is native. ARM64 images and the complete learning journey execute under QEMU on x86-64; native ARM hardware/performance is untested. Windows 11/WSL commands are documented but physical Windows execution is untested. The exact CI script passes locally from a clean clone; hosted GitHub Actions was not triggered. Optional paid model calls, public deployment and remote database TLS were not performed. Accessibility combines automated scans, keyboard/zoom/touch checks and actual local Orca/AT-SPI inspection, with tools and limitations recorded; no user research or other screen-reader/platform certification is claimed.
