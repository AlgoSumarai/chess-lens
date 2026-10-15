# Stockfish 19 corresponding source and build materials

The distributed engines are **unmodified official sf_19 binaries**, launched
as separate UCI processes. ChessLens does not link them into its assemblies.
The exact release tag resolves to
`edb0d9db6731067ec50ce619ff372b463bc4dd5d` (see `tag.json`). The official
binary archives retain `Copying.txt`, `AUTHORS`, `src/`, `scripts/` and wiki
build documentation. The supplemental source archive also retains upstream's
`.github/workflows/` and compiler/build configuration.

| Material | SHA-256 |
| --- | --- |
| Linux x86-64 universal release archive | `9defc0d4e55d49c65a6d042f3e571a39fcea499ade6dbe741b53b8c65e03611f` |
| Linux ARM64 universal release archive | `fe26cfd1d9db4c8af3d21e24d9ff34cacb31c1f940085a7583da11796f2bac01` |
| Windows x86-64 universal release archive | `3c8bf1f9ea66a09350a40df4f632288285ac206d99f33ab5842c408fc30b48a7` |
| Commit source tar archive | `4fc20699a5ecf7883b036ce180020f5e57979623ad719fd2a52839209521581b` |
| `nn-1a298aa575a0.nnue` | `1a298aa575a085434d29027978dc36867fe9c5bcea9376654b7a8eba1e52dfc2` |

Sources:

- [Official binary release](https://github.com/official-stockfish/Stockfish/releases/tag/sf_19)
- [Exact source archive](https://codeload.github.com/official-stockfish/Stockfish/tar.gz/edb0d9db6731067ec50ce619ff372b463bc4dd5d)
- [Exact network](https://tests.stockfishchess.org/api/nn/nn-1a298aa575a0.nnue)
- [Pinned upstream build recipe](https://github.com/official-stockfish/Stockfish/blob/edb0d9db6731067ec50ce619ff372b463bc4dd5d/.github/workflows/universal_compilation.yml)

The 82 source/build files present in each Linux binary archive match this commit
byte for byte. The commit archive additionally includes `scripts/.gitattributes`;
that metadata file is absent from the binary archive. The network was also
exported from the distributed x86-64 executable and its full SHA-256 matches.

Keep the supplemental source directory and its `src/nn-1a298aa575a0.nnue` beside
the binary when distributing the prepared worker or installation. A source-only
release should include the source tar archive **and the full network**, with
these notices. A link to the original binary alone is insufficient.

For a local functional build on Linux with GCC 15 or later, GNU make and
`hexdump`, start in the supplemental source directory and run:

```sh
make -j2 -C src ARCH=x86-64-universal build
# On native ARM64 instead:
make -j2 -C src ARCH=arm64-universal build
```

The included network prevents the upstream downloader from being needed. Its
unmodified `scripts/net.sh` includes a `curl -k` fallback; ChessLens installers
and Docker downloads verify TLS and a full SHA-256 and never invoke that fallback.

These functional build commands do not promise byte-identical release output.
Upstream's supplied `universal_compilation.yml` documents GCC 15, `profile-build`,
stripping, and architecture checks. The x86 profiling job uses Intel SDE 9.58.0;
the ARM64 profiling job runs on native ARM and uses QEMU for its architecture
checks. Those external compiler/system tools are not bundled by ChessLens.

Stockfish source and its NNUE evaluation are distributed under GPLv3; preserve
`GPL-3.0.txt` and `AUTHORS`. Stockfish's README acknowledges neural networks
trained using data provided by the [Leela Chess Zero project](https://storage.lczero.org/files/training_data),
made available under the [Open Database License](https://opendatacommons.org/licenses/odbl/1-0/).
The full ODbL text is retained here. The original Leela training database is not
bundled; this is an acknowledgement of upstream training-data provenance.
ChessLens includes no opening database.
