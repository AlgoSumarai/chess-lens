# Third-party notices

ChessLens uses the following open-source components. Dependency lockfiles are the authoritative version inventory. [The licence bundle](licenses/README.md) contains the complete application dependency texts, per-package sources and SHA-256 receipts. `python3 scripts/licenses.py` checks the bundle against the exact locks without network access.

- .NET / ASP.NET Core / EF Core / SignalR: Microsoft and contributors, MIT.
- PostgreSQL: PostgreSQL Global Development Group, PostgreSQL License.
- Npgsql: Npgsql contributors, PostgreSQL License.
- Gera.Chess: Sviatoslav Harasymchuk and contributors, MIT. https://github.com/Geras1mleo/Chess/blob/master/LICENSE.md
- React / React DOM: Meta Platforms and contributors, MIT.
- Vite, TypeScript, Tailwind, Vitest, Playwright: respective upstream contributors; see distributed package licence files (MIT or Apache-2.0 as applicable).
- react-chessboard: Copyright (c) 2022 Ryan Gregory, MIT. https://github.com/Clariity/react-chessboard
- chess.js: Jeff Hlywa and contributors, BSD-2-Clause. https://github.com/jhlywa/chess.js
- nginx: nginx contributors, BSD-2-Clause.

Board piece graphics are Cburnett-derived default pieces from react-chessboard 5.12.1. All twelve authoritative Wikimedia file pages attribute Cburnett and explicitly offer a choice including the three-clause BSD licence. ChessLens selects that BSD option; the package code remains MIT. Paths, groups, dimensions and styling have been adapted. [Per-piece sources and distributed SVG hashes](licenses/board-provenance.json) and the [full artwork copyright/conditions](licenses/board-pieces-BSD-3-Clause.txt) are bundled. The browser's **Licences & artwork** link delivers all production npm licence texts and this attribution.

Sample PGNs in `samples/` are original, artificial ChessLens fixtures. They are labelled as local demo games and are not provider integration evidence. Opening labels in those fixtures are sample metadata; no opening database is bundled.

Stockfish 19 is used as a separate UCI process under GPLv3. Installers and Linux x86-64/ARM64 worker images use checksum-pinned official release archives. Those archives include `Copying.txt`, `AUTHORS`, corresponding `src/` and build documentation. The full extracted directory is retained beside the executable (`.local/stockfish-19/stockfish` locally; `/opt/stockfish` in the worker image). A supplemental `corresponding-source` directory includes the complete pinned source commit, upstream compiler/build workflows, the exact embedded NNUE network, licence/author notices and training-data acknowledgement. The source and network downloads have full SHA-256 checks. See [exact source/build materials](licenses/stockfish/BUILD.md) and the [official release](https://github.com/official-stockfish/Stockfish/releases/tag/sf_19).

Stockfish acknowledges neural networks trained with [Leela Chess Zero training data](https://storage.lczero.org/files/training_data), made available under ODbL. The [complete ODbL text](licenses/stockfish/ODbL-1.0.txt) is retained; the original training database is not bundled.

Application-library texts are bundled now. `scripts/collect-image-notices.py`
exports the actual runtime images' complete installed Debian/Ubuntu package
copyright files, common licence texts and available .NET runtime notices, with
exact package/source versions, image IDs and SHA-256 hashes. CI preserves that
bundle in `.local/ci/image-notices`; retain it with any distributed image set.
[Dependency documentation](docs/dependencies.md) links the distribution and image
source archives. Final release verification separately collects both
architectures' notices, checks the built artifacts and records release digests.
