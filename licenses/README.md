# Bundled third-party materials

`application.json` records **28 non-development npm lock entries plus Vite and
Tailwind's generated runtime helpers/preflight CSS** and **63
unique application-project NuGet entries**, including transitive/design tools.
Each record identifies the version, original declaration/source and complete
licence/notice texts under `bundled/`. Identical texts are stored once by
SHA-256. `upstream/` preserves reviewed supplements from pinned package
repositories where the package archive does not contain its main licence.
Nuspec copyright statements and original additional notices are retained.

Run `python3 scripts/licenses.py` for an offline exact-lock/content check.
After a dependency update, review new licence terms and upstream attribution,
run `npm ci` and `dotnet restore --locked-mode`, then regenerate with
`python3 scripts/licenses.py --write`. The updater makes no network calls and
fails if a required reviewed supplement is missing. Do not label a missing
licence as MIT based on another package's declaration.

The browser distribution includes `/licenses/third-party-notices.txt`, containing
all 30 selected npm dependency texts plus the separate board-artwork BSD
copyright and conditions. `/licenses/board-provenance.json` maps all twelve
Cburnett-derived default pieces to authoritative source pages and records the
exact rendered react-chessboard 5.12.1 SVG hashes. Adaptations include path
rounding, grouping, dimensions and styling. All twelve source pages offer the
BSD option. Unicode data terms are included for the Node-side `tr46` dependency;
the inventory conservatively includes all non-dev dependencies, even where a
browser build does not use their Node-only paths.

API and worker images retain this directory under `/app/licenses`. Stockfish's
full source/network/build materials live separately beside its executable at
`/opt/stockfish/corresponding-source`; see [the exact build information](stockfish/BUILD.md).
Verify an extracted Linux installation with:

```sh
python3 scripts/check-stockfish-materials.py \
  --directory .local/stockfish-19/stockfish --architecture amd64 \
  --output .local/stockfish-materials.json
```

Use `--architecture arm64` for that binary. Real engine behavior is separately
checked by `scripts/check-engine-image.py`; a source-hash pass alone does not
establish engine or application behavior.

Other build/test tools independently downloaded by the documented startup/CI
commands are not redistributed in the frontend or application publish output. Their
upstream licences remain in their own distributions. Operating-system and .NET
runtime components in the final base images have a separate image inventory;
final image-notice extraction and release-artifact inspection remain release
gates. Keep those notices when redistributing images. No opening database is
included, and all bundled sample PGNs are original artificial fixtures.
