#!/usr/bin/env python3
"""Verify isolated local HTTPS, proxy/cookie boundaries, DB roles and key persistence.

Uses the supplied CA for real chain/hostname verification; never disables TLS
verification or installs a trust root. Restarts only the selected validation API.
"""
import argparse
from datetime import datetime, timezone
import http.cookiejar
from http.cookies import SimpleCookie
import json
from pathlib import Path
import re
import ssl
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--url", required=True)
    parser.add_argument("--http-url", required=True)
    parser.add_argument("--ca", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    secure, plain = (urllib.parse.urlparse(u) for u in (args.url, args.http_url))
    if not re.fullmatch(r"chesslens-[a-z0-9-]+-validation", args.project) or secure.scheme != "https" or plain.scheme != "http" or secure.hostname not in ("localhost", "127.0.0.1") or plain.hostname != secure.hostname or secure.username or plain.username:
        parser.error("Select loopback HTTPS/HTTP URLs and an isolated chesslens-*-validation project.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    api_container = args.project + "-api-1"

    def docker(*command, check=True):
        return subprocess.run(["docker", "--context", "default", *command], text=True, capture_output=True, check=check, timeout=45)

    engine = docker("info", "--format", "{{.OperatingSystem}}").stdout
    if "Docker Desktop" in engine:
        parser.error("A native Linux Docker Engine is required.")
    for service in ("api", "worker", "frontend", "database"):
        label = docker("inspect", "--format", '{{index .Config.Labels "com.docker.compose.project"}}', args.project + "-" + service + "-1").stdout.strip()
        if label != args.project:
            parser.error("Validation service ownership does not match the selected project.")

    evidence = {"startedUtc": datetime.now(timezone.utc).isoformat(), "project": args.project,
                "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
                "worktreeDirty": bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip()), "checks": []}

    def record(name, **values):
        evidence["checks"].append({"name": name, **values})
        args.output.write_text(json.dumps(evidence, indent=2) + "\n")
        print(name, json.dumps(values), flush=True)

    context = ssl.create_default_context(cafile=str(args.ca))
    jar = http.cookiejar.CookieJar()
    client = urllib.request.build_opener(urllib.request.HTTPSHandler(context=context), urllib.request.HTTPCookieProcessor(jar), NoRedirect())
    token = None

    def request(path, body=None, method="GET", headers=None, base=None):
        fields = {"Content-Type": "application/json", **(headers or {})}
        if token and method != "GET":
            fields["X-CSRF-TOKEN"] = token
        req = urllib.request.Request((base or args.url) + path, data=None if body is None else json.dumps(body).encode(), headers=fields, method=method)
        try:
            response = client.open(req, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read(2 * 1024 * 1024)
            try:
                value = json.loads(raw) if raw else None
            except ValueError:
                value = None
            return response.code, value, response.headers

    def api(path, body=None, method="GET", headers=None):
        status, value, response_headers = request("/api" + path, body, method, headers)
        if status >= 400:
            raise AssertionError(f"{method} {path} returned HTTP {status}")
        return value, response_headers

    def wait_ready():
        deadline = time.monotonic() + 150
        while time.monotonic() < deadline:
            try:
                if request("/health/ready")[0] == 200:
                    return
            except (urllib.error.URLError, TimeoutError):
                pass
            time.sleep(1)
        raise TimeoutError("TLS application readiness did not recover.")

    registered = False
    try:
        wait_ready()
        try:
            urllib.request.urlopen(args.url + "/health/ready", timeout=15).close()
        except urllib.error.URLError as error:
            if not isinstance(error.reason, ssl.SSLCertVerificationError):
                raise
        else:
            raise AssertionError("The private test CA unexpectedly exists in the ordinary system trust store.")
        record("certificate-chain-and-hostname", trustedCaRequest=200, ordinaryUntrustedClientRejected=True)
        status, _, headers = request("/health/ready?local-check=1", base=args.http_url)
        assert status == 308 and headers["Location"] == args.url + "/health/ready?local-check=1"
        record("http-redirect", status=status, exactSecureAuthority=True, pathAndQueryPreserved=True)
        assert request("/health/ready", headers={"Host": "unrelated.test"})[0] == 421
        status, _, headers = request("/health/ready")
        assert status == 200 and headers.get("Strict-Transport-Security") == "max-age=31536000"
        record("host-and-security-headers", foreignHostRejected=421, hstsPresent=True)

        csrf, headers = api("/account/csrf")
        token = csrf["token"]
        cookies = SimpleCookie()
        for line in headers.get_all("Set-Cookie", []):
            cookies.load(line)
        crumb = cookies["__Host-chesslens.csrf"]
        assert crumb["secure"] and crumb["httponly"] and crumb["samesite"].lower() == "strict" and crumb["path"] == "/" and not crumb["domain"]
        registered_user, headers = api("/account/register", {"email": f"https-fixture-{uuid.uuid4().hex}@test.example", "password": "ChessLens.HTTPS2026!" + uuid.uuid4().hex}, "POST")
        registered = True
        cookies = SimpleCookie()
        for line in headers.get_all("Set-Cookie", []):
            cookies.load(line)
        session = cookies["__Host-chesslens.session"]
        assert session["secure"] and session["httponly"] and session["samesite"].lower() == "strict" and session["path"] == "/" and not session["domain"]
        owner = api("/account/me")[0]["id"]
        token = None
        assert request("/api/account/logout", {}, "POST")[0] == 400
        assert request("/hubs/progress/negotiate?negotiateVersion=1", {}, "POST", {"Origin": "https://unrelated.test"})[0] == 403
        assert request("/hubs/progress/negotiate?negotiateVersion=1", {}, "POST", {"Origin": args.url})[0] == 200
        # Supplying a false scheme cannot override the TLS edge's own values.
        assert api("/account/me", headers={"X-Forwarded-Proto": "http", "X-Forwarded-For": "198.51.100.99", "X-Forwarded-Host": "unrelated.test"})[0]["id"] == owner
        record("cookies-csrf-origins-and-forwarding", secureHostOnlyCookies=True, httpOnly=True, sameSite="Strict",
               missingCsrfRejected=400, foreignOriginRejected=403, ownedNegotiation=200, edgeOverwritesClientForwarding=True)

        rows = docker("exec", args.project + "-database-1", "psql", "-X", "-At", "-U", "chesslens_admin", "-d", "chesslens", "-v", "ON_ERROR_STOP=1", "-c",
                      'SELECT json_build_object(\'superuser\', rolsuper, \'createDatabase\', rolcreatedb, \'createRoles\', rolcreaterole, \'schemaCreate\', has_schema_privilege(rolname,\'public\',\'CREATE\')) FROM pg_roles WHERE rolname=\'chesslens\';').stdout.strip()
        role = json.loads(rows)
        assert role == {"superuser": False, "createDatabase": False, "createRoles": False, "schemaCreate": False}
        record("runtime-database-role", **role, actualAccountWritesSucceeded=True)
        docker("restart", "--time", "2", api_container)
        wait_ready()
        assert api("/account/me")[0]["id"] == owner
        record("persistent-protection-keys", originalSecureCookieAfterApiRestart=200, sameOwner=True)
        evidence["status"] = "passed"
    except Exception as error:
        evidence["status"] = "failed"
        record("failure", errorType=type(error).__name__)
        raise
    finally:
        docker("start", api_container, check=False)
        if registered:
            try:
                wait_ready()
                token = api("/account/csrf")[0]["token"]
                api("/account/data", method="DELETE")
                evidence["fixtureDataDeleted"] = True
            except Exception:
                evidence["fixtureDataDeleted"] = False
        evidence["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        args.output.write_text(json.dumps(evidence, indent=2) + "\n")


if __name__ == "__main__":
    main()
