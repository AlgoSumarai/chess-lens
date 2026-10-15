#!/usr/bin/env python3
"""Create three local production role secrets in a new private directory."""
import argparse
import os
from pathlib import Path
import secrets

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--directory", type=Path, default=Path(".local/production-secrets"))
args = parser.parse_args()
args.directory.parent.mkdir(parents=True, exist_ok=True)
args.directory.mkdir(mode=0o700)  # Deliberately refuses an existing directory.
os.chmod(args.directory, 0o700)
for name in ("admin-password", "app-password", "migration-password"):
    path = args.directory / name
    with path.open("x") as stream:
        stream.write(secrets.token_hex(32) + "\n")
    # The private parent guards host access; per-file read-only bind mounts let
    # the non-root container UID read only the secret granted to that service.
    os.chmod(path, 0o444)
print("Created distinct administrator, migration and runtime secrets in the new private directory.")
