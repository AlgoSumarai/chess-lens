#!/usr/bin/env python3
"""Back up a native Compose database and keys; restore only to new isolated targets."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import uuid


def utc():
    return datetime.now(timezone.utc).isoformat()


def docker(*arguments, **kwargs):
    return subprocess.run(["docker", "--context", "default", *arguments], check=True,
                          stderr=subprocess.PIPE, timeout=600, **kwargs)


def output(*arguments):
    return docker(*arguments, stdout=subprocess.PIPE, text=True).stdout.strip()


def container(project, service):
    ids = output("ps", "--filter", f"label=com.docker.compose.project={project}",
                 "--filter", f"label=com.docker.compose.service={service}", "--format", "{{.ID}}").splitlines()
    if len(ids) != 1:
        raise ValueError(f"Expected one running {service} in project {project}.")
    return ids[0]


def private_file(path):
    return os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "wb")


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


class Snapshot:
    """Keep one read-only exported snapshot open for both pg_dump and fingerprints."""
    def __init__(self, database_container, database):
        self.process = subprocess.Popen([
            "docker", "--context", "default", "exec", "-i", "-e", "PGCONNECT_TIMEOUT=10",
            database_container, "psql", "-X", "-A", "-t", "-q", "-U", "chesslens",
            "-d", database, "-v", "ON_ERROR_STOP=1"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            self.query("BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY; SET LOCAL TIME ZONE 'UTC'; SET LOCAL statement_timeout='120s';")
        except Exception:
            self.close()
            raise

    def query(self, sql):
        marker = "CHESSLENS_BACKUP_QUERY_END"
        self.process.stdin.write(sql + f"\nSELECT '{marker}';\n")
        self.process.stdin.flush()
        rows = []
        while True:
            line = self.process.stdout.readline()
            if not line:
                raise RuntimeError("Backup snapshot query failed; no backup is declared complete.")
            line = line.rstrip("\n")
            if line == marker:
                return rows
            rows.append(line)

    def fingerprints(self):
        tables = self.query("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;")
        result = {}
        for table in tables:
            # Hash each canonical JSON row, then the sorted row hashes, preserving duplicates.
            rows = self.query(f"""SELECT json_build_object('rows', count(*), 'sha256',
                encode(sha256(convert_to(coalesce(string_agg(digest, '' ORDER BY digest), ''), 'UTF8')), 'hex'))
                FROM (SELECT encode(sha256(convert_to(to_jsonb(t)::text, 'UTF8')), 'hex') AS digest
                FROM public.{identifier(table)} t) contents;""")
            result[table] = json.loads(rows[0])
        return result

    def close(self):
        if self.process.poll() is None:
            self.process.stdin.close()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=10)
        for stream in (self.process.stdout, self.process.stderr):
            stream.close()


@contextmanager
def snapshot(database_container, database):
    transaction = None
    try:
        transaction = Snapshot(database_container, database)
        yield transaction
    finally:
        if transaction:
            transaction.close()


def validate_keys(path):
    keys = {}
    with tarfile.open(path) as archive:
        for member in archive:
            name = member.name.removeprefix("./")
            if member.isdir() and name in ("", "."):
                continue
            if not member.isfile() or not re.fullmatch(r"key-[a-fA-F0-9-]{36}\.xml", name):
                raise ValueError("Keys archive contains an unexpected path or link.")
            if name in keys or member.size > 1024 * 1024:
                raise ValueError("Keys archive contains a duplicate or oversized key.")
            with archive.extractfile(member) as stream:
                keys[name] = hashlib.sha256(stream.read()).hexdigest()
    return keys


def create(args, database_container):
    api = container(args.project, "api")
    target = args.output.resolve()
    target.mkdir(mode=0o700, parents=False, exist_ok=False)
    started = utc()
    with snapshot(database_container, "chesslens") as transaction:
        version = transaction.query("SHOW server_version;")[0]
        exported = transaction.query("SELECT pg_export_snapshot();")[0]
        if not re.fullmatch(r"[A-Fa-f0-9]+-[A-Fa-f0-9]+-[0-9]+", exported):
            raise ValueError("Unexpected PostgreSQL snapshot identifier.")
        tables = transaction.fingerprints()
        with private_file(target / "database.dump") as stream:
            docker("exec", database_container, "pg_dump", "-U", "chesslens", "-d", "chesslens",
                   "--format=custom", "--no-owner", "--no-acl", f"--snapshot={exported}", stdout=stream)
    with private_file(target / "protection-keys.tar") as stream:
        docker("exec", api, "tar", "-C", "/app/keys", "-cf", "-", ".", stdout=stream)
    manifest = {
        "format": 1, "startedUtc": started, "completedUtc": utc(), "project": args.project,
        "database": "chesslens", "postgresVersion": version,
        "apiImage": output("inspect", "--format", "{{.Image}}", api),
        "files": {name: sha256(target / name) for name in ("database.dump", "protection-keys.tar")},
        "tables": tables, "keys": validate_keys(target / "protection-keys.tar"),
        "scope": "Single application database and plaintext Data Protection keys. Cluster roles/configuration and external secrets are separate.",
    }
    with private_file(target / "manifest.json") as stream:
        stream.write((json.dumps(manifest, indent=2) + "\n").encode())
    print(f"Backup complete: {target}; {len(tables)} tables, {len(manifest['keys'])} protection keys.")


def restore(args, database_container):
    if not re.fullmatch(r"chesslens_restore_[a-z0-9_]{1,40}", args.database):
        raise ValueError("Use a new database named chesslens_restore_<name>.")
    if not re.fullmatch(r"chesslens-restore-[a-z0-9-]{1,45}-keys", args.keys_volume):
        raise ValueError("Use a new keys volume named chesslens-restore-<name>-keys.")
    source = args.source.resolve()
    manifest = json.loads((source / "manifest.json").read_text())
    if manifest.get("format") != 1 or set(manifest.get("files", {})) != {"database.dump", "protection-keys.tar"}:
        raise ValueError("Unsupported or incomplete backup manifest.")
    for name, digest in manifest["files"].items():
        if sha256(source / name) != digest:
            raise ValueError(f"Backup integrity mismatch: {name}.")
    if validate_keys(source / "protection-keys.tar") != manifest["keys"]:
        raise ValueError("Protection key integrity mismatch.")
    exists = output("exec", database_container, "psql", "-X", "-A", "-t", "-U", "chesslens", "-d", "postgres",
                    "-c", f"SELECT datname FROM pg_database WHERE datname='{args.database}';")
    volumes = output("volume", "ls", "--format", "{{.Name}}").splitlines()
    if exists or args.keys_volume in volumes:
        raise ValueError("Restore targets must both be new. Existing databases and volumes are never overwritten.")
    output("exec", database_container, "psql", "-X", "-U", "chesslens", "-d", "postgres", "-v", "ON_ERROR_STOP=1",
           "-c", f"CREATE DATABASE {identifier(args.database)} TEMPLATE template0;")
    with (source / "database.dump").open("rb") as stream:
        docker("exec", "-i", database_container, "pg_restore", "-U", "chesslens", "-d", args.database,
               "--no-owner", "--no-acl", "--exit-on-error", stdin=stream, stdout=subprocess.DEVNULL)
    with snapshot(database_container, args.database) as transaction:
        restored = transaction.fingerprints()
    if restored != manifest["tables"]:
        raise ValueError("Restored table counts/content differ from the exported backup snapshot; targets retained for inspection.")
    api = container(args.project, "api")
    # A running container can outlive its untagged image after a rebuild/prune.
    # The current local API tag supplies only tar/chown; restored bytes are checked independently.
    image = output("inspect", "--format", "{{.Config.Image}}", api)
    helper_image_id = output("image", "inspect", "--format", "{{.Id}}", image)
    output("volume", "create", "--label", "chesslens.restore=true", args.keys_volume)
    with (source / "protection-keys.tar").open("rb") as stream:
        docker("run", "--pull=never", "--rm", "-i", "--network", "none", "--read-only", "--cap-drop", "ALL",
               "--security-opt", "no-new-privileges", "--user", "0",
               "--mount", f"type=volume,source={args.keys_volume},target=/restore", "--entrypoint", "tar",
               image, "-C", "/restore", "-xf", "-", "--no-same-owner", stdin=stream, stdout=subprocess.DEVNULL)
    # Make the fresh volume accessible to the runtime app user, with private file permissions.
    output("run", "--pull=never", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL", "--cap-add", "CHOWN",
           "--security-opt", "no-new-privileges", "--user", "0",
           "--mount", f"type=volume,source={args.keys_volume},target=/restore", "--entrypoint", "sh", image,
           "-c", 'chmod 700 /restore && for key in /restore/key-*.xml; do [ ! -e "$key" ] || chmod 600 "$key" || exit 1; done; chown -R app:app /restore')
    archive = source / f"restored-{args.database}-keys.tar"
    with private_file(archive) as stream:
        docker("run", "--pull=never", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL",
               "--security-opt", "no-new-privileges", "--user", "app",
               "--mount", f"type=volume,source={args.keys_volume},target=/restore,readonly", "--entrypoint", "tar",
               image, "-C", "/restore", "-cf", "-", ".", stdout=stream)
    if validate_keys(archive) != manifest["keys"]:
        raise ValueError("Restored protection keys differ; fresh targets retained for inspection.")
    receipt = {"verifiedUtc": utc(), "database": args.database, "keysVolume": args.keys_volume,
               "tables": restored, "keys": manifest["keys"], "helperImage": helper_image_id,
               "manifestSha256": sha256(source / "manifest.json")}
    with private_file(source / f"restore-{args.database}.json") as stream:
        stream.write((json.dumps(receipt, indent=2) + "\n").encode())
    print(f"Restore verified: {args.database}, {args.keys_volume}; {len(restored)} tables and {len(receipt['keys'])} keys match.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default="chesslens")
    commands = parser.add_subparsers(dest="command", required=True)
    backup = commands.add_parser("create")
    backup.add_argument("--output", required=True, type=Path)
    recovery = commands.add_parser("restore")
    recovery.add_argument("--source", required=True, type=Path)
    recovery.add_argument("--database", required=True)
    recovery.add_argument("--keys-volume", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"chesslens(?:-[a-z0-9-]+)?", args.project):
        parser.error("Select the exact chesslens Compose project.")
    engine = output("info", "--format", "{{.OSType}} {{.OperatingSystem}}")
    if not engine.startswith("linux ") or "Docker Desktop" in engine:
        raise ValueError("A native Linux Docker Engine in context default is required.")
    database_container = container(args.project, "database")
    os.umask(0o077)
    try:
        if args.command == "create":
            create(args, database_container)
        else:
            restore(args, database_container)
    except subprocess.CalledProcessError as error:
        directory = args.output.resolve() if args.command == "create" else args.source.resolve()
        diagnostic = directory / f"failure-{uuid.uuid4().hex}.log"
        if directory.is_dir():
            with private_file(diagnostic) as stream:
                detail = error.stderr or b"No diagnostic output."
                stream.write(detail.encode() if isinstance(detail, str) else detail)
            raise RuntimeError(f"Docker/PostgreSQL command failed; private diagnostic: {diagnostic}") from error
        raise


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, OSError, subprocess.SubprocessError) as error:
        # Never emit command stderr: restore errors can include private database contents.
        detail = str(error) if isinstance(error, (ValueError, RuntimeError)) else type(error).__name__
        print(f"Backup/restore failed: {detail}. No success is recorded; existing application targets are preserved.", file=sys.stderr)
        sys.exit(1)
