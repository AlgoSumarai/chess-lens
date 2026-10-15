#!/usr/bin/env python3
"""Build or check the offline application dependency licence bundle.

--write needs npm ci and locked .NET restore first. It never fetches remote
licences: reviewed upstream supplements and their SHA-256 receipts are committed.
The default check needs only this checkout, so CI can detect lock/bundle drift.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DIRECTORY = ROOT / "licenses"
CATALOGUE = DIRECTORY / "application.json"
PUBLIC = ROOT / "web/public/licenses/third-party-notices.txt"
# These dev dependencies contribute preflight CSS or runtime preload helpers to
# the actual production bundle. Include their notices as well as runtime deps.
BUNDLED_BUILD_INPUTS = {"node_modules/tailwindcss", "node_modules/vite"}


def include_npm(key, package):
    return bool(key) and (not package.get("dev") or key in BUNDLED_BUILD_INPUTS)


def sha256(content):
    return hashlib.sha256(content).hexdigest()


def lock_paths():
    return [ROOT / "web/package-lock.json", *sorted((ROOT / "src").glob("*/packages.lock.json"))]


def lock_hashes():
    return {str(p.relative_to(ROOT)): sha256(p.read_bytes()) for p in lock_paths()}


def expected_packages():
    result = set()
    for key, package in json.loads((ROOT / "web/package-lock.json").read_text())["packages"].items():
        if include_npm(key, package):
            result.add(("npm", key, package["version"]))
    for path in lock_paths()[1:]:
        for dependencies in json.loads(path.read_text())["dependencies"].values():
            for name, package in dependencies.items():
                if package["type"] != "Project":
                    result.add(("nuget", name, package["resolved"]))
    return result


def notice_file(path):
    return path.is_file() and path.name.lower().startswith(("licen", "copying", "notice", "thirdparty", "third-party"))


def public_text(catalogue):
    parts = ["ChessLens browser dependencies and artwork — full licence texts\n",
             (DIRECTORY / "board-pieces-BSD-3-Clause.txt").read_text(),
             "Board source inventory: /licenses/board-provenance.json\n"]
    for package in catalogue["packages"]:
        if package["ecosystem"] != "npm":
            continue
        parts.append(f"\n{'=' * 72}\n{package['name']} {package['version']}\n{package['license']}\n")
        for reference in package["texts"]:
            parts.extend([f"\nSource: {reference['source']}\n", (ROOT / reference["path"]).read_text(encoding="utf-8-sig")])
    return "\n".join(parts).encode()


def write():
    upstream = json.loads((DIRECTORY / "upstream/sources.json").read_text())
    for filename, receipt in upstream.items():
        if sha256((DIRECTORY / "upstream" / filename).read_bytes()) != receipt["sha256"]:
            raise ValueError(f"Reviewed upstream text changed: {filename}")
    bundled = DIRECTORY / "bundled"
    bundled.mkdir(exist_ok=True)

    def reference(path, source):
        content = path.read_bytes()
        digest = sha256(content)
        extension = ".rtf" if path.suffix.lower() == ".rtf" else ".txt"
        target = bundled / (digest + extension)
        target.write_bytes(content)
        return {"path": str(target.relative_to(ROOT)), "sha256": digest, "source": source}

    def supplement(filename):
        return reference(DIRECTORY / "upstream" / filename, upstream[filename]["source"])

    packages = []
    npm = json.loads((ROOT / "web/package-lock.json").read_text())["packages"]
    overrides = {"@microsoft/signalr": "signalr-10.0.0-MIT.txt", "fetch-cookie": "fetch-cookie-2.2.0-Unlicense.txt", "tr46": "tr46-0.0.3-MIT.txt"}
    for key, package in sorted(npm.items()):
        if not include_npm(key, package):
            continue
        folder = ROOT / "web" / key
        metadata = json.loads((folder / "package.json").read_text())
        if metadata["version"] != package["version"]:
            raise ValueError(f"npm ci is required: {key}")
        texts = [reference(p, f"npm:{metadata['name']}@{package['version']}/{p.relative_to(folder)}")
                 for p in sorted(folder.rglob("*"))
                 if "node_modules" not in p.relative_to(folder).parts and notice_file(p)]
        if metadata["name"] in overrides:
            texts.insert(0, supplement(overrides[metadata["name"]]))
        if metadata["name"] == "tr46":
            texts.append(supplement("Unicode-Data.txt"))
        if not texts:
            raise ValueError(f"A reviewed full licence is required for {key}")
        packages.append({"ecosystem": "npm", "key": key, "name": metadata["name"], "version": package["version"],
                         "license": package["license"], "integrity": package["integrity"], "texts": texts})

    cache = Path(os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
    for _, name, version in sorted(p for p in expected_packages() if p[0] == "nuget"):
        folder = cache / name.lower() / version.lower()
        metadata = ET.parse(folder / f"{name.lower()}.nuspec").find("{*}metadata")
        repository = metadata.find("{*}repository")
        if repository is None or not repository.get("commit"):
            raise ValueError(f"A reviewed source supplement is required for {name}")
        repo_url = repository.get("url").removesuffix(".git")
        filename = repo_url.removeprefix("https://github.com/").replace("/", "-") + "-" + repository.get("commit")[:12] + ".txt"
        texts = [supplement(filename)]
        texts.extend(reference(p, f"nuget:{name}@{version}/{p.name}") for p in sorted(folder.iterdir()) if notice_file(p))
        license_node = metadata.find("{*}license")
        packages.append({"ecosystem": "nuget", "key": name, "name": name, "version": version,
                         "license": license_node.text if license_node is not None else "MIT (reviewed upstream repository)",
                         "copyright": metadata.findtext("{*}copyright", ""),
                         "repository": dict(repository.attrib), "texts": texts})
    catalogue = {"schemaVersion": 1, "scope": "All non-dev npm lock entries plus Tailwind/Vite generated CSS/runtime helpers, and all application-project NuGet lock entries including design tools; excludes separately downloaded test/build tools and base-image OS/runtime components.",
                 "lockSha256": lock_hashes(), "packages": packages}
    CATALOGUE.write_text(json.dumps(catalogue, indent=2, ensure_ascii=False) + "\n")
    PUBLIC.parent.mkdir(parents=True, exist_ok=True)
    PUBLIC.write_bytes(public_text(catalogue))
    for filename in ["board-provenance.json", "board-pieces-BSD-3-Clause.txt"]:
        (PUBLIC.parent / filename).write_bytes((DIRECTORY / filename).read_bytes())
    # Keep only referenced generated copies; reviewed upstream originals stay intact.
    used = {r["path"] for p in packages for r in p["texts"]}
    for path in bundled.iterdir():
        if str(path.relative_to(ROOT)) not in used:
            path.unlink()


def check():
    catalogue = json.loads(CATALOGUE.read_text())
    if catalogue["lockSha256"] != lock_hashes():
        raise ValueError("Dependency locks changed. Review licences and regenerate the bundle with --write.")
    actual = {(p["ecosystem"], p["key"], p["version"]) for p in catalogue["packages"]}
    if actual != expected_packages() or len(actual) != len(catalogue["packages"]):
        raise ValueError("Licence inventory does not match the exact application dependency closure.")
    for package in catalogue["packages"]:
        if not package["texts"]:
            raise ValueError("Missing full licence text.")
        for ref in package["texts"]:
            path = (ROOT / ref["path"]).resolve()
            if not path.is_relative_to(DIRECTORY.resolve()) or sha256(path.read_bytes()) != ref["sha256"]:
                raise ValueError(f"Licence content mismatch for {package['name']}.")
    if PUBLIC.read_bytes() != public_text(catalogue):
        raise ValueError("Distributed browser notices are stale.")
    for filename in ["board-provenance.json", "board-pieces-BSD-3-Clause.txt"]:
        if (PUBLIC.parent / filename).read_bytes() != (DIRECTORY / filename).read_bytes():
            raise ValueError("Distributed artwork attribution is stale.")
    counts = {eco: sum(p["ecosystem"] == eco for p in catalogue["packages"]) for eco in ("npm", "nuget")}
    print(f"Exact dependency licence inventory and bundled text hashes match: {counts}.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    if parser.parse_args().write:
        write()
    check()
