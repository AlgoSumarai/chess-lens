#!/usr/bin/env python3
"""Verify the exact Linux binary, source, network, build recipe and notices."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True, help="Extracted stockfish directory beside the executable.")
    parser.add_argument("--architecture", choices=("amd64", "arm64"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    materials = args.directory / "corresponding-source"
    notices = materials / "chesslens-notices"
    manifest = json.loads((notices / "source-verification.json").read_text())
    record = next(r for r in manifest if r["architecture"] == args.architecture)
    if record["commit"] != "edb0d9db6731067ec50ce619ff372b463bc4dd5d":
        raise ValueError("Unexpected corresponding-source commit.")
    binary_name, expected_binary = {
        "amd64": ("stockfish-linux-x86-64-universal", "0f83d24cc46d2c66c60f16001af5444873bc112b7d028594513426894c12da19"),
        "arm64": ("stockfish-linux-arm64-universal", "bb6599ef38b7a4ae79200a601c40e81a854219a49dba78b1110e7eb4620609bf"),
    }[args.architecture]
    if sha256(args.directory / binary_name) != expected_binary or sha256(args.directory / "stockfish") != expected_binary:
        raise ValueError("The distributed binary or stable executable alias differs.")
    for item in record["matchedFiles"]:
        for root in (args.directory, materials):
            if sha256(root / item["path"]) != item["sha256"]:
                raise ValueError(f"Source file mismatch: {item['path']}")
    network = materials / "src/nn-1a298aa575a0.nnue"
    if sha256(network) != "1a298aa575a085434d29027978dc36867fe9c5bcea9376654b7a8eba1e52dfc2":
        raise ValueError("The full NNUE source network is missing or different.")
    for filename in ("universal_compilation.yml", "upload_binaries.yml"):
        if (materials / ".github/workflows" / filename).read_bytes() != (notices / filename).read_bytes():
            raise ValueError("The pinned upstream build recipe differs.")
    if (materials / "Copying.txt").read_bytes() != (notices / "GPL-3.0.txt").read_bytes():
        raise ValueError("The Stockfish GPL text differs.")
    if (materials / "AUTHORS").read_bytes() != (notices / "AUTHORS").read_bytes():
        raise ValueError("The Stockfish authors notice differs.")
    for filename in ("BUILD.md", "ODbL-1.0.txt"):
        if not (notices / filename).is_file():
            raise ValueError("Missing build or training-data attribution materials.")
    # Engine behavior is checked separately with real UCI by check-engine-image.py.
    receipt = {"testedUtc": datetime.now(timezone.utc).isoformat(), "architecture": args.architecture,
               "commit": record["commit"], "matchingReleaseSourceFiles": len(record["matchedFiles"]),
               "binarySha256": expected_binary,
               "networkSha256": sha256(network), "upstreamBuildRecipesPresent": True,
               "licenceAndAuthorNoticesPresent": True,
               "scope": "Read-only corresponding-source/material checks; not a native-hardware, byte-reproducibility or throughput claim."}
    args.output.write_text(json.dumps(receipt, indent=2) + "\n")
    print(f"Verified {len(record['matchedFiles'])} release source/build files and the complete source/network/notices for {args.architecture}.")


if __name__ == "__main__":
    main()
