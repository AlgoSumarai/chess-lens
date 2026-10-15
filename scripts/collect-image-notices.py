#!/usr/bin/env python3
"""Export/verify installed Debian/Ubuntu image copyrights and runtime notices.

Containers run read-only, offline, without capabilities or application volumes.
No credentials/configuration from running services are inspected. Stockfish and
application dependency materials have their separate exact-source inventories.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tarfile
import uuid


def docker(*args, **kwargs):
    return subprocess.run(["docker", "--context", "default", *args], check=True, stderr=subprocess.PIPE, timeout=120, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", action="append", required=True)
    parser.add_argument("--platform", choices=("linux/amd64", "linux/arm64"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    engine = docker("info", "--format", "{{.OSType}} {{.OperatingSystem}}", stdout=subprocess.PIPE, text=True).stdout
    if not engine.startswith("linux ") or "Docker Desktop" in engine:
        raise ValueError("Use native Linux Docker Engine.")
    args.output.mkdir(parents=True, exist_ok=False)
    results = []
    for image in args.image:
        if not re.fullmatch(r"[a-zA-Z0-9][a-zA-Z0-9./:_@-]+", image):
            raise ValueError("Select an explicit local image reference.")
        metadata = json.loads(docker("image", "inspect", "--platform", args.platform, image, stdout=subprocess.PIPE, text=True).stdout)[0]
        name = "chesslens-notices-" + uuid.uuid4().hex
        base = ["run", "--pull=never", "--rm", "--name", name, "--platform", args.platform, "--network", "none",
                "--read-only", "--user", "0", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--memory", "128m", "--cpus", "1"]

        def execute(entrypoint, arguments, **kwargs):
            try:
                return docker(*base, "--entrypoint", entrypoint, image, *arguments, **kwargs)
            finally:
                subprocess.run(["docker", "--context", "default", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)

        package_text = execute("dpkg-query", ["-W", "-f=${db:Status-Abbrev}\t${binary:Package}\t${Version}\t${source:Package}\t${source:Version}\n"], stdout=subprocess.PIPE, text=True).stdout
        packages = []
        for line in package_text.splitlines():
            status, binary, version, source, source_version = line.split("\t")
            if not status.startswith("ii"):
                continue
            packages.append({"package": binary, "version": version, "sourcePackage": source, "sourceVersion": source_version,
                             "copyright": "usr/share/doc/" + binary.split(":")[0] + "/copyright"})
        if not packages:
            raise ValueError("No installed-package inventory was available.")
        label = re.sub(r"[^a-zA-Z0-9_.-]", "_", image) + "-" + metadata["Id"].removeprefix("sha256:")[:12]
        directory = args.output / label
        directory.mkdir()
        archive = directory / "notices.tar"
        # All exported paths contain only static distribution documentation.
        script = """set -eu
set -- "$@" usr/share/common-licenses etc/os-release
if [ -f /usr/share/dotnet/LICENSE.txt ]; then
  set -- "$@" usr/share/dotnet/LICENSE.txt usr/share/dotnet/ThirdPartyNotices.txt
fi
tar -C / --hard-dereference -chf - "$@"
"""
        with archive.open("xb") as stream:
            execute("sh", ["-c", script, "notices", *(p["copyright"] for p in packages)], stdout=stream)
        content = directory / "contents"
        content.mkdir()
        with tarfile.open(archive) as tar:
            tar.extractall(content, filter="data")
        missing = [p["package"] for p in packages if not (content / p["copyright"]).is_file()]
        if missing:
            raise ValueError(f"Missing copyright texts in {image}: {', '.join(missing)}")
        files = {}
        for path in sorted(content.rglob("*")):
            if path.is_file():
                files[str(path.relative_to(content))] = hashlib.sha256(path.read_bytes()).hexdigest()
        result = {"image": image, "imageId": metadata["Id"], "platform": args.platform, "repoDigests": metadata.get("RepoDigests", []),
                  "packages": packages, "files": files, "archiveSha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
                  "scope": "Complete installed package copyright/common-licence files and .NET runtime notices where present. Application and exact Stockfish sources are separate."}
        (directory / "inventory.json").write_text(json.dumps(result, indent=2) + "\n")
        results.append(result)
        print(f"Verified {len(packages)} package copyright files and {len(files)} documentation/notice files in {image} ({args.platform}).")
    (args.output / "manifest.json").write_text(json.dumps({"collectedUtc": datetime.now(timezone.utc).isoformat(), "images": results}, indent=2) + "\n")


if __name__ == "__main__":
    main()
