# Local validation of the HTTPS deployment

`compose.production.yaml` is a single-host production-oriented example. It
uses the same application/worker, persistent database and Data Protection keys,
with TLS at nginx, exact hosts/origins, one explicitly trusted proxy and
separate database roles. All published ports bind to loopback by default.
Preparing or exercising this example does not publish the application.

Use native Linux Docker Engine, the Compose plugin, Python 3 and OpenSSL. On
Windows 11 use the documented Ubuntu WSL Engine workflow. Windows execution
has not been reverified in this Linux session.

## Configuration, keys and first startup

Copy `deploy/production.env.example` to `.local/production.env` and review the
public hostname/authority, ports, certificate paths and unused bridge subnet.
`CHESSLENS_PROXY_IP` must belong to that subnet and is reserved for nginx.
Set the exact authority with its port when HTTPS uses a non-default port.
Neither wildcard hosts nor HTTP browser origins are accepted in Production.

For local verification only:

```sh
python3 scripts/init-production-secrets.py
bash scripts/init-local-tls.sh
docker --context default compose --env-file .local/production.env \
  -p chesslens-production -f compose.production.yaml build
docker --context default compose --env-file .local/production.env \
  -p chesslens-production -f compose.production.yaml up -d --no-build \
  --wait --wait-timeout 240
```

The initializers refuse existing destinations. The private secret directory is
mode `0700`; the three distinct random password files are read-only (`0444`)
inside it so Docker can mount the selected file for the non-root application
UID. The parent prevents other host users from traversing the directory. Each
container receives only its required secrets; the database initializer needs
all three to establish the roles. Passwords do not appear in Compose environment
values or connection strings. Avoid commands that print secret-file contents.

The local certificate helper creates a seven-day private CA and a leaf
certificate for `localhost`/`127.0.0.1`. It changes no trust store. For an actual
deployment, supply the appropriate existing certificate chain/private key for
the configured hostname. Retain the private-key directory's permissions and
replace/renew certificates before expiry; reload nginx after replacement.
The CA private key is a local test artifact and is never mounted into nginx.

Do not attach this new role layout to an existing development database volume.
Preserve that volume and restore a verified copy with the role-aware procedure
below. Initial role scripts run only for a new PostgreSQL data directory.

The runtime database role `chesslens` has DML access, without superuser,
database/role creation or schema creation privileges. `chesslens_migrator` owns
application objects and supplies migrations; `chesslens_admin` is reserved for
database initialization/operations. The database publishes no host port.
Its password-authenticated bridge connection is private to this single Engine
host. For a separately hosted database, configure its verified TLS connection
and CA; no remote-database TLS validation is claimed by this local example.

## HTTP boundary

nginx redirects public HTTP to the configured HTTPS authority and retains path
and query. Its TLS server rejects unrelated Host headers. It overwrites client
forwarding headers with the actual immediate peer/scheme and preserves the
original Host/port for both API and SignalR requests. API processing accepts
forwarded address/scheme values only from the configured literal proxy IP,
with one hop and matching header counts. Other forwarded-host/prefix values
are not consumed. Authentication and rate limiting run after that boundary.

Production API/training routes require HTTPS. Session and antiforgery cookies
use `__Host-` names, `Secure`, `HttpOnly`, `SameSite=Strict`, path `/` and no
Domain attribute. CSRF tokens remain required for API mutations. SignalR still
requires authentication, exact browser origins and owner authorization.
Development/Testing retain loopback HTTP behavior.

Internal health checks use loopback HTTP. The API host filter also allows the
private `127.0.0.1` health caller; nginx enforces the public authority before
any external request reaches it. nginx's container-only health listener binds
to `127.0.0.1:8081` and is never published. HTTPS returns HSTS, CSP, framing and
content-type protections. The initial full-stack attempt exposed the missing
internal health host; that diagnostic and corrected startup are retained.

## Verification

After starting an **isolated validation project**, with matching URLs/paths:

```sh
python3 scripts/check-https.py --project chesslens-https-validation \
  --url https://localhost:8443 --http-url http://localhost:8080 \
  --ca .local/local-tls/ca.pem --output .local/https-check.json
```

The verifier checks the actual certificate chain and hostname with the supplied
CA, confirms the ordinary untrusted client rejects that CA, exercises redirects,
hosts, secure cookies, CSRF, origins, forwarding-header replacement, actual
runtime database privileges and the same authenticated cookie after an API
restart with persistent keys. It creates only original test-account data,
cleans that data and never removes volumes or changes global TLS trust.

Browser journeys can use `CHESSLENS_LOCAL_TEST_CERTIFICATE=1` **only** with a
loopback HTTPS URL. That flag permits the private test certificate in the
disposable Playwright browser context; it does not verify its chain. Run the
independent CA/hostname verifier first. No application/provider/model TLS
verification is disabled. With a normally trusted deployment certificate, omit
the flag. The verification ledger distinguishes those browser checks from the
actual certificate validation and from public deployment.

## Upgrades and restoring a copy

Keep the same project/volumes/secrets for normal restarts. Build new images
first, back up, then stop API and worker **before** the migration service runs.
Start matching versions with `up --no-build --wait` afterward. Do not rotate a
password file without updating the corresponding PostgreSQL role through a
separate controlled maintenance procedure. Replacing a file does not reset a
password in an existing database volume.

Production backups use the administrative role over the container's local
connection. The role's password is not put on the command line:

```sh
python3 scripts/backup.py --project chesslens-production \
  --database-user chesslens_admin create --output .local/production-backup
python3 scripts/backup.py --project chesslens-production \
  --database-user chesslens_admin restore --source .local/production-backup \
  --database chesslens_restore_reviewed --keys-volume chesslens-restore-reviewed-keys \
  --owner-role chesslens_migrator --runtime-role chesslens
```

Both restore targets must be new. The existing roles must already exist;
their passwords, TLS keys and cluster settings are separate from the logical
backup. The helper restores objects under the migration owner and grants
runtime DML/default privileges only in the new database, then verifies **all
table counts/content hashes as the runtime role**, plus key-file hashes/private
permissions. It does not modify original application data or role privileges
in the original database. See [backup/restore details](backup-restore.md).

For a reviewed cutover to that copy, set `CHESSLENS_RESTORE_DATABASE` and
`CHESSLENS_RESTORE_KEYS_VOLUME` in the private configuration, and append
`-f deploy/restore-override.example.yaml` to the same project's Compose commands.
Build first, stop old API/worker writers, apply migrations against the selected
copy, then start and check readiness, authentication, ownership, a new real
engine job and immutable saved evidence. Keep the original targets for recovery.
Do not infer a completed cutover from the logical restore receipt alone.

Backups include plaintext private game data and authentication keys. They stay
in private directories with mode `0600` files. Use host storage encryption and
an appropriate protected off-host retention policy for real operations.
Container Data Protection keys persist privately in the named key volume;
this example does not configure an external key-encryption service.
