# Third-party notices

ChessLens uses the following open-source components. Dependency lockfiles are the authoritative version inventory.

- .NET / ASP.NET Core / EF Core / SignalR: Microsoft and contributors, MIT.
- PostgreSQL: PostgreSQL Global Development Group, PostgreSQL License.
- Npgsql: Npgsql contributors, PostgreSQL License.
- Gera.Chess: Sviatoslav Harasymchuk and contributors, MIT. https://github.com/Geras1mleo/Chess/blob/master/LICENSE.md
- React / React DOM: Meta Platforms and contributors, MIT.
- Vite, TypeScript, Tailwind, Vitest, Playwright: respective upstream contributors; see distributed package licence files (MIT or Apache-2.0 as applicable).
- react-chessboard: Copyright (c) 2022 Ryan Gregory, MIT. https://github.com/Clariity/react-chessboard
- chess.js: Jeff Hlywa and contributors, BSD-2-Clause. https://github.com/jhlywa/chess.js
- nginx: nginx contributors, BSD-2-Clause.

Board piece graphics currently come from react-chessboard's default piece set. Verify the upstream artwork provenance and include any separate attribution before release; the package's MIT licence alone is not treated as proof of every asset's provenance.

Sample PGNs in `samples/` are original, artificial ChessLens fixtures. They are labelled as local demo games and are not provider integration evidence. Opening labels in those fixtures are sample metadata; no opening database is bundled.

Stockfish 19 is used as a separate UCI process under GPLv3. The Windows installation script and Linux worker image use checksum-pinned official release archives. Those archives include `Copying.txt`, `AUTHORS`, corresponding `src/` and build documentation. The full extracted directory is retained beside the executable (`.local/stockfish-19/stockfish` locally; `/opt/stockfish` in the worker image). Release packaging must preserve those materials and confirm source/build completeness for the exact binaries distributed. Upstream: https://github.com/official-stockfish/Stockfish/releases/tag/sf_19

Release preparation must bundle complete applicable licence texts with distributable artifacts; this inventory is not a substitute for those texts.
