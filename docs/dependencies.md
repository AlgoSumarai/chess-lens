# Dependencies and source checks

Versions selected 2026-09-13. Exact direct references and transitive lockfiles are committed. Use `dotnet restore --locked-mode` and `npm ci` for reproducible dependency resolution.

| Component | Version | Basis |
| --- | --- | --- |
| .NET / ASP.NET / EF Identity | 10.0.12 | [.NET 10 LTS support](https://dotnet.microsoft.com/en-us/platform/support/policy); NuGet registry confirmed servicing packages |
| Container SDK | 10.0.401 | [Official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) |
| Local minimum SDK | 10.0.203, latestFeature roll-forward | Available installed SDK; container is the patched release build path |
| PostgreSQL | 18.6 | Official Docker image pulled and migrations applied |
| Npgsql EF provider | 10.0.3 | NuGet stable 10.x; compatible with EF 10.0.12 |
| Gera.Chess | 1.2.0 | [Upstream rules and SAN documentation](https://github.com/Geras1mleo/Chess); MIT; regression tests cover critical special moves |
| Stockfish | 19 (`sf_19`) | [Official release](https://github.com/official-stockfish/Stockfish/releases/tag/sf_19), universal CPU binaries, pinned SHA256 archives, complete archive/source/licence retained |
| React / React DOM | 19.2.8 | npm stable release |
| Vite / React plugin | 8.2.2 / 6.1.1 | [Vite requirements](https://vite.dev/guide/); installed Node 24.15.0 meets them |
| TypeScript | 5.9.3 | Pinned compatible compiler |
| Tailwind | 4.3.3 | npm stable release and Vite integration |
| react-chessboard | 5.12.1 | [Upstream package](https://github.com/Clariity/react-chessboard); MIT |
| chess.js | 1.4.0 | Client-side legal variations; server remains authoritative |
| SignalR client | 10.0.0 | Owner-scoped progress notifications with HTTP reconciliation |
| Vitest / Playwright | 5.0.0 / 1.63.0 | Component behavior and real browser journeys |
| nginx | 1.30.4-alpine | [Official image manifest](https://github.com/docker-library/official-images/blob/master/library/nginx) |

The selected PostgreSQL image resolved locally to `postgres@sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280`. Image tags are version-pinned; record and lock all release image digests during release preparation.

Authentication follows [ASP.NET Core antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0): HttpOnly session cookie and a request token header for every API mutation. Cookie tokens are refreshed after authentication identity changes.

This Windows environment's TLS-intercepting certificate was available in the system trust store. `NODE_OPTIONS=--use-system-ca` enabled npm and Playwright downloads without disabling certificate verification. This is an environment-specific development setting, not a committed TLS bypass.
