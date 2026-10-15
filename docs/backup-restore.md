# Backup and isolated restore

Use Python 3 and the native Linux Docker Engine `default` context, from the repository root. On Windows 11, run these commands inside the documented Ubuntu WSL 2 checkout. The script selects the exact Compose project by service labels. It never reads a password into a command argument or prints database diagnostics to the console.

For the HTTPS role layout, add `--database-user chesslens_admin` before the
`create`/`restore` command. Restore with `--owner-role chesslens_migrator
--runtime-role chesslens` to retain separate migration ownership and runtime
DML privileges in the new database. Both roles must already exist. The helper
checks all restored table fingerprints as that runtime role. Role secrets and
cluster configuration are deliberately separate from the backup. See the
[deployment and copy-cutover instructions](deployment.md#upgrades-and-restoring-a-copy).

Keep backups in a private directory on the Linux filesystem. They contain account password hashes, original private PGNs, saved evidence and Data Protection key material. A filesystem permission mask does not encrypt a backup; use your protected backup storage and keep its access separate from public release artifacts. Do not place a backup in a Git checkout tracked directory or a public artifact store.

```sh
mkdir -m 700 -p .local/backups
python3 scripts/backup.py --project chesslens create --output .local/backups/2026-10-14
```

The output directory must be new. Files are created with mode `0600` and the directory with mode `0700`. A completed backup contains `database.dump`, `protection-keys.tar` and `manifest.json`. The manifest is written last; a directory without it is incomplete. Check command exit status and preserve failure diagnostics privately.

The database dump and table fingerprints use the **same exported, read-only repeatable-read snapshot**, so normal concurrent application writes do not invalidate verification. For every public application table, the manifest records its row count and a SHA-256 digest of sorted canonical JSON row hashes, including duplicate rows. It also records SHA-256 file hashes and individual key-file hashes. This checks corruption and consistency; it does not authenticate a manifest obtained from an untrusted source. PostgreSQL's [snapshot export](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-SNAPSHOT-SYNCHRONIZATION), [pg_dump snapshot option](https://www.postgresql.org/docs/18/app-pgdump.html) and [binary SHA-256 function](https://www.postgresql.org/docs/18/functions-binarystring.html) define these operations.

Data Protection keys are copied after the database snapshot. ChessLens does not remove old keys during routine rotation; keep all keys needed for existing cookies. Key names and content hashes are validated, and the archive cannot contain links or paths outside the expected key files. If you configure an external certificate or key store to protect the XML at rest, retain the corresponding certificate/private key or key-store recovery procedure separately.

Restore into **new** targets in the selected running database service:

```sh
python3 scripts/backup.py --project chesslens restore \
  --source .local/backups/2026-10-14 \
  --database chesslens_restore_20261014 \
  --keys-volume chesslens-restore-20261014-keys
```

Existing databases and volumes are refused. Restore never uses `--clean`, changes the original application's database/volume, or points the running application at the restored data. It validates the backup files before creating targets, restores with `pg_restore --exit-on-error`, compares every table to the exported snapshot, restores keys with private permissions for the container's `app` user, and compares their contents again. A private `restore-<database>.json` receipt is written only after all checks pass. A failed restore retains its new targets for inspection and does not produce a success receipt.

The small key-copy helpers use the current locally available API image tag with `--pull=never`, no network and a read-only root filesystem. Only the fresh key volume is writable; ownership setup adds the `CHOWN` capability temporarily. The helper image supplies file utilities and does not run the API or migrations. The receipt records its image ID. A running API container can outlive an image removed by a rebuild/prune, so its old image ID is not assumed to remain launchable.

The restored database is a point-in-time copy. Pending work may have unexpired leases, and copied readiness rows are historical. Before any cutover, use a separately configured isolated API/worker and the restored key volume, apply the intended release migrations, check accounts, representative games/reports and readiness, and verify pending-job recovery. Do not start a worker against the copy until its external provider/model configuration is understood. Change production routing only through your normal deployment procedure after local validation.

This is a single-application logical backup, not point-in-time recovery for a PostgreSQL cluster. Cluster roles, server configuration, external secrets, TLS certificate material and image/source release artifacts are separate. The [PostgreSQL backup documentation](https://www.postgresql.org/docs/18/backup.html) describes cluster backup and WAL recovery options. Use a backup from your trusted application database; a PostgreSQL restore executes its saved database definitions.

The dated live backup/restore results, including failed attempts, are in [verification](verification.md). Native Linux execution is verified; a separate native Windows Python/PowerShell execution is not claimed.
