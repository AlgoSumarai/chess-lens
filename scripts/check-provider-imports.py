#!/usr/bin/env python3
"""Opt-in live public import/resync check against a running local ChessLens.

Creates a labelled local test account, fetches at most two completed public games
per provider, verifies immutable resync, and removes its imported data afterwards.
Unavailable providers are reported as unverified gates and cause exit code 1.
"""
import argparse
import datetime as dt
import http.cookiejar
import json
import secrets
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://localhost:8088")
    parser.add_argument("--from-date", default="2026-09-01")
    parser.add_argument("--to-date", default="2026-10-01", help="Exclusive UTC end date")
    parser.add_argument("--chesscom", default="hikaru")
    parser.add_argument("--lichess", default="german11")
    parser.add_argument("--output", default=".local/evidence/providers/live-import-resync.json")
    args = parser.parse_args()
    parsed = urllib.parse.urlparse(args.url)
    if parsed.scheme != "http" or parsed.hostname not in ("localhost", "127.0.0.1"):
        parser.error("Use the loopback HTTP development application for this local verification.")
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    token = None

    def request(path, body=None, method="GET"):
        nonlocal token
        data = None if body is None else json.dumps(body).encode()
        headers = {"Content-Type": "application/json"}
        if method != "GET":
            headers["X-CSRF-TOKEN"] = token
        req = urllib.request.Request(args.url + "/api" + path, data=data, headers=headers, method=method)
        with client.open(req, timeout=30) as response:
            content = response.read()
            return json.loads(content) if content else None

    def wait(job_id):
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            job = request("/jobs/" + job_id)
            if job["state"] not in ("queued", "running"):
                return job
            time.sleep(1)
        request("/jobs/" + job_id + "/cancel", method="POST")
        raise TimeoutError("Import did not finish within 180 seconds; cancellation requested.")

    evidence = {"checkedAt": dt.datetime.now(dt.timezone.utc).isoformat(), "kind": "live public provider integration",
                "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
                "worktreeDirty": bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip()),
                "url": args.url, "from": args.from_date, "toExclusive": args.to_date, "maximumGames": 2, "providers": []}
    token = request("/account/csrf")["token"]
    request("/account/register", {"email": f"live-provider-{secrets.token_hex(8)}@test.example", "password": "LiveProvider!2026" + secrets.token_hex(8)}, "POST")
    token = request("/account/csrf")["token"]
    try:
        for provider, username in (("chesscom", args.chesscom), ("lichess", args.lichess)):
            result = {"provider": provider, "username": username, "status": "unverified"}
            try:
                payload = {"provider": provider, "username": username, "from": args.from_date + "T00:00:00Z", "to": args.to_date + "T00:00:00Z", "maxGames": 2}
                first = wait(request("/imports/provider", payload, "POST")["id"])
                result["firstJob"] = first
                if first["state"] != "completed" or first["imported"] != 2 or first["failures"]:
                    raise AssertionError("Two valid completed public games were not imported.")
                library = request("/games?source=" + provider)
                before = {g["id"]: request("/games/" + g["id"]) for g in library["items"]}
                if len(before) != 2 or any(g["playerColour"] is None for g in before.values()):
                    raise AssertionError("Expected two imported games with a selected player colour.")
                second = wait(request("/imports/provider", payload, "POST")["id"])
                result["resyncJob"] = second
                if second["state"] != "completed" or second["imported"] != 0 or second["duplicates"] != 2 or second["failures"]:
                    raise AssertionError("Resync did not preserve exactly two owner associations.")
                after = request("/games?source=" + provider)
                if after["total"] != 2 or {g["id"] for g in after["items"]} != set(before):
                    raise AssertionError("Resync changed the source identities.")
                for game_id, original in before.items():
                    if request("/games/" + game_id) != original:
                        raise AssertionError("Resync changed an immutable source game.")
                result.update(status="passed", games=[{"id": g["id"], "url": g["url"], "plies": len(g["moves"]),
                    "ratingPool": g["ratingPool"], "hasRatings": g["whiteRating"] is not None and g["blackRating"] is not None,
                    "clockMoves": sum(m["clockSeconds"] is not None for m in g["moves"]), "opening": g["opening"]} for g in before.values()])
            except (AssertionError, TimeoutError, urllib.error.URLError) as error:
                result["limitation"] = str(error)
            evidence["providers"].append(result)
            print(provider + ": " + result["status"] + (" — " + result.get("limitation", "") if result["status"] != "passed" else ""), flush=True)
    finally:
        request("/account/data", method="DELETE")
        evidence["importedTestDataDeleted"] = True
        output = Path(args.output); output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(evidence, indent=2) + "\n")
    return 0 if all(p["status"] == "passed" for p in evidence["providers"]) else 1


if __name__ == "__main__":
    raise SystemExit(main())
