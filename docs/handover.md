# Local handover — 15 October 2026

**Local release accepted:** independent Round 2 **PASS, 8.545/10**, with every dimension above 8.0 and no unresolved critical/high defect. Two rounds were used and the review loop stopped. Round 1's failure and all three corrected import/recovery findings remain in [the exact review record](quality-review.md).

The running application is http://localhost:8088. It uses native Linux Docker Engine's `default` context, project `chesslens-engine-validation`, database port 54339 and the original local `.env`, database and authentication-key volumes. Open the application, create/sign in to your account and follow [the demonstration](demo.md). No paid model key is required.

## Preserve the existing installation

This host's validation project name differs from the generic fresh-install default. Keep that project name and its volumes when restarting. Run from the repository root with Docker group access (a fresh login may be needed):

```sh
DATABASE_PORT=54339 APP_PORT=8088 docker --context default compose --env-file .env \
  -p chesslens-engine-validation -f compose.yaml -f compose.app.yaml \
  -f .local/releases/2026.10.15-reviewed/compose-amd64.yaml stop api worker
DATABASE_PORT=54339 APP_PORT=8088 docker --context default compose --env-file .env \
  -p chesslens-engine-validation -f compose.yaml -f compose.app.yaml \
  -f .local/releases/2026.10.15-reviewed/compose-amd64.yaml \
  run --rm --no-deps migrate
DATABASE_PORT=54339 APP_PORT=8088 docker --context default compose --env-file .env \
  -p chesslens-engine-validation -f compose.yaml -f compose.app.yaml \
  -f .local/releases/2026.10.15-reviewed/compose-amd64.yaml \
  up -d --no-build --pull never --wait --wait-timeout 240
```

These commands use the already loaded release images and preserve the existing password and storage. The equivalent stop → migrate → no-build/no-pull restart was actually performed after review: all 33 table hashes and both authentication-key hashes were unchanged, all four services use the exact release references, and readiness returned 200. The candidate/final Compose overrides select the same images. Private receipts remain in `.local/evidence/handover/final-native-upgrade/`. Back up before an upgrade; [backup/restore](backup-restore.md) and [HTTPS/copy cutover](deployment.md) cover data and persistent keys. [Offline startup](release.md) covers a separate fresh installation with unused ports. The local HTTPS example at https://localhost:8443 uses a private test CA and a restored database; it is separate from the normal HTTP app.

## Release and evidence

The final handover packet is `.local/releases/2026.10.15-reviewed/`: committed source including both exact review reports and safe evidence, x86-64/ARM64 image archives, Compose overrides, full runtime notices and `release.json`/`SHA256SUMS`. Image version remains `2026.10.15-r2`; runtime application inputs remain `3b3ae4a808cc2dda5e1ce789ccab2da8558cbf86`. The manifest identifies and hashes the final committed source archive; it is authoritative because an archive cannot contain its own subsequent hash. The packager rejects changed runtime inputs. The earlier corrected candidate `.local/releases/2026.10.15-r2/` and failed Round 1 packet `.local/releases/2026.10.15-local/` remain separate historical artifacts.

Use [the acceptance matrix](acceptance-matrix.md), [verification ledger](verification.md), [independent review](quality-review.md), [release record](release-record.md) and [benchmark](benchmark.md) for checks and their limits. Raw logs/TRX/browser reports/traces/screenshots and receipts remain in the named `.local/evidence/` paths; current clean-checkout artifacts remain in `.local/clean-checkout-review2/.local/ci/` and its `web/` reports. Private database dumps, credentials and TLS/key material are excluded from release archives.

The delivered scope includes both public provider imports/resync, grounded no-key/optional-model coaching, conversion/endgame findings, saved mistake practice, immutable weekly reports/comparable progress, connected desktop/mobile screens, ownership/resource/recovery controls and operational release tooling. Licence texts, board artwork provenance and complete exact Stockfish binary/source/network/build materials accompany the distributions.

Current clean-checkout checks pass **227 backend / 10 component / 34 native desktop-mobile cases**, build and dependency audits; native accessibility has **64 zero-violation scans**. Six fresh ARM64 cases include both complete journeys and **40 zero-violation scans**. The reviewer independently passes the full 227 backend, components/build, both actual desktop/mobile journeys and two labelled terminal-rendering fixtures, with **36 further zero-violation scans**. The measured mixed workload completes six reviews/182 plies, six imports and all five side lanes in **222.009 seconds**; consult its hardware/cache/load limits before comparing timing.

| Independent dimension | Score |
| --- | ---: |
| Code quality/maintainability | 8.5 |
| Chess/domain/feature correctness | 8.7 |
| Ease/interface quality | 8.2 |
| Reliability/security/accessibility | 8.7 |
| Verification/handover | 8.9 |
| **Weighted overall** | **8.545** |

Two low-priority observations remain: saved progress highlights Weekly reports in navigation, and review/findings pages could place lessons ahead of lengthy diagnostics. Both are recorded in the independent report and its interface score.

## Validation limits

Linux x86-64 is native. ARM64 images and the complete learning journey execute under QEMU on x86-64; native ARM hardware/performance is untested. Windows 11/WSL commands are documented but physical Windows execution is untested. The exact CI script passes locally from a clean clone; hosted GitHub Actions was not triggered. Optional paid model calls, public deployment and remote database TLS were not performed. Accessibility combines automated scans, keyboard/zoom/touch checks and actual local Orca/AT-SPI inspection, with tools and limitations recorded; no user research or other screen-reader/platform certification is claimed.
