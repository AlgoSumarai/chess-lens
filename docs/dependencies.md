# Dependencies and source checks

Current selections reviewed 2026-10-15. Exact direct references and transitive lockfiles are committed. Use `dotnet restore --locked-mode` and `npm ci` for reproducible dependency resolution. Dated observations below retain their original versions.

| Component | Version | Basis |
| --- | --- | --- |
| .NET / ASP.NET / EF Identity | 10.0.13 | [.NET 10 LTS support](https://dotnet.microsoft.com/en-us/platform/support/policy); NuGet registry confirmed servicing packages |
| Container SDK | 10.0.402 | [Official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) |
| Local SDK | 10.0.402, latestPatch roll-forward | Matches the tested container SDK; older feature bands are no longer selected |
| PostgreSQL | 18.6 | Official Docker image pulled and migrations applied |
| Npgsql EF provider | 10.0.3 | NuGet stable 10.x; compatible with EF 10.0.13 |
| Gera.Chess | 1.2.0 | [Upstream rules and SAN documentation](https://github.com/Geras1mleo/Chess); MIT; regression tests cover critical special moves |
| Stockfish | 19 (`sf_19`) | [Official release](https://github.com/official-stockfish/Stockfish/releases/tag/sf_19), universal CPU binaries, pinned SHA256 archives, complete archive/source/licence retained |
| React / React DOM | 19.2.8 | npm stable release |
| Node build tools | 24.22.0 LTS | [Official release](https://nodejs.org/en/blog/release/v24.22.0); compatible update within the existing LTS line |
| Vite / React plugin | 8.2.2 / 6.1.1 | [Vite requirements](https://vite.dev/guide/); installed Node 24.22.0 meets them |
| TypeScript | 5.9.3 | Pinned compatible compiler |
| Tailwind | 4.3.3 | npm stable release and Vite integration |
| react-chessboard | 5.12.1 | [Upstream package](https://github.com/Clariity/react-chessboard); MIT |
| chess.js | 1.4.0 | Client-side legal variations; server remains authoritative |
| SignalR client | 10.0.0 | Owner-scoped progress notifications with HTTP reconciliation |
| Vitest / Playwright | 5.0.0 / 1.63.0 | Component behavior and real browser journeys |
| axe-core / Playwright integration | 4.13.0 | [Official accessibility integration](https://playwright.dev/docs/accessibility-testing); pinned automated scans |
| nginx | 1.30.6-trixie | [Official stable image manifest](https://github.com/docker-library/official-images/blob/master/library/nginx); current security servicing and packaged copyright texts |

The selected PostgreSQL image resolved locally to `postgres@sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280`. Image tags are version-pinned; record and lock all release image digests during release preparation.

Authentication follows [ASP.NET Core antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0): HttpOnly session cookie and a request token header for every API mutation. Cookie tokens are refreshed after authentication identity changes.

This Windows environment's TLS-intercepting certificate was available in the system trust store. `NODE_OPTIONS=--use-system-ca` enabled npm and Playwright downloads without disabling certificate verification. This is an environment-specific development setting, not a committed TLS bypass.


## Fresh security checks — 13 October 2026

The [official .NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), fetched on this date, lists runtime **10.0.12** and SDK **10.0.401** as latest for the channel. The containers already use them; `global.json` now selects the same SDK feature band with servicing-only roll-forward. The local `dotnet-ef` tool is pinned to **10.0.12**. The earlier local-minimum feature band is no longer selected.

`dotnet list ChessLens.slnx package --vulnerable --include-transitive --format json --no-restore` reports no flagged NuGet packages across all four projects, using the official NuGet source. The full npm audit initially reported one high-severity advisory in the transitive development dependency `source-map-js` **1.2.1**, used by CSS/source-map and jsdom tooling. [The reviewed advisory](https://github.com/advisories/GHSA-68fv-2mgg-jv7q) and [upstream 1.2.2 release](https://github.com/7rulnik/source-map-js/releases/tag/v1.2.2) identify **1.2.2** as the fix for excessive indexed source-map offsets. This is a development-tool dependency finding; no exploit against ChessLens's PGN API is claimed.

A targeted `npm update source-map-js --ignore-scripts --no-fund` changes only that lock entry, within existing compatible ranges; direct dependencies are unchanged. Fresh `npm ci` and `npm audit` then report **zero known vulnerabilities**. Component/build/container/browser validation is recorded in the verification ledger. Audit results are dated feed observations, not proof that the dependency graph has no unknown defects. Final release checks still include container/architecture/licence inventory and reproducible CI.

## SignalR servicing update — 14 October 2026

The fresh ARM64 restore rejected `Microsoft.AspNetCore.SignalR.Common` 10.0.12 with NU1903. [Microsoft's CVE-2026-70159 advisory](https://github.com/advisories/GHSA-334f-vvp4-7g6p), published 13 October, identifies stateful-reconnect broadcast/resource retention and fixes the .NET 10 package in **10.0.13**. Yesterday's cached audit observation remains historical. The application uses ordinary automatic reconnect, but the affected shared framework and client/test dependency are updated rather than suppressing the advisory.

Current [official .NET metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) lists runtime/ASP.NET **10.0.13** and SDK **10.0.402**. Docker bases, global SDK minimum, .NET Microsoft package references and both EF tool manifests now align on those servicing versions. Locks are regenerated with the intended package updates, then verified with locked restore. The SDK archive's official SHA-512 is checked before extraction. Fresh transitive NuGet audit reports no flagged packages; all 206 backend tests pass. The patched native stack reports readiness and passes 28 browser cases. These are dated security-feed observations, not a claim about unknown vulnerabilities.

## Frontend tooling and server servicing — 15 October 2026

Node **24.22.0 LTS**, released 14 October, replaces 24.15.0 in the build stage
and CI. The older pin predates the [available July security fixes](https://nodejs.org/en/blog/vulnerability/july-2026-security-releases), including HTTP/2 memory and TLS-agent verification issues. Node is used for development/build tools; nginx serves the resulting static application. The isolated Linux archive matches the official SHA-256 manifest. Application dependency versions and locks are unchanged. The [announced October 21 security release](https://nodejs.org/en/blog/vulnerability/october-2026-security-releases) is future work, not an available update or a vulnerability claimed fixed by 24.22.0.

The frontend uses **nginx 1.30.6-trixie**, replacing 1.30.4-alpine. The
[official security advisories](https://nginx.org/en/security_advisories.html)
identify 1.30.6 as the fixed stable release for alias/rewrite and variable-script
buffer overflows, HTTP/3 body injection, TLS-ticket forgery and AIO use-after-free.
Some affected optional features are unused here; no exploit against this app is
claimed. Debian's variant retains copyright texts for every installed package,
which the Alpine runtime image did not. Both supported architectures are in the
official image manifest. Health checks now use the installed curl utility.

`scripts/collect-image-notices.py` exports each selected image's exact package
versions, source-package versions, complete packaged copyright/common-licence
texts and .NET licence/third-party notices. It records image IDs, available
registry digests and SHA-256 hashes. Debian source packages are available through
[Debian Sources](https://sources.debian.org/); Ubuntu package sources through
[Ubuntu Launchpad](https://launchpad.net/ubuntu/+source). nginx's additional
package and build recipes are supplied by the [official image source](https://github.com/nginx/docker-nginx).
Release packaging must retain these exports alongside the application and exact
Stockfish materials. The preliminary native export succeeds for all four images;
the final release image/architecture exports remain separately recorded gates.
