#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
case "$(uname -m)" in
  x86_64) chesslens_engine_arch=x86-64; chesslens_engine_sha=9defc0d4e55d49c65a6d042f3e571a39fcea499ade6dbe741b53b8c65e03611f ;;
  aarch64|arm64) chesslens_engine_arch=arm64; chesslens_engine_sha=fe26cfd1d9db4c8af3d21e24d9ff34cacb31c1f940085a7583da11796f2bac01 ;;
  *) echo 'Supported Linux Stockfish architectures: x86-64 and ARM64.' >&2; exit 1 ;;
esac
chesslens_engine_directory="${1:-.local/stockfish-19}"
if [[ -e "$chesslens_engine_directory" ]]; then
  echo 'Select a new installation directory; an existing directory is never overwritten.' >&2
  exit 1
fi
mkdir -p -- "$(dirname -- "$chesslens_engine_directory")"
mkdir -- "$chesslens_engine_directory"
chesslens_engine_archive="$chesslens_engine_directory/stockfish-linux-$chesslens_engine_arch-universal.tar.gz"
curl --fail --location --proto '=https' --tlsv1.2 --retry 2 --connect-timeout 15 --max-time 180 \
  "https://github.com/official-stockfish/Stockfish/releases/download/sf_19/stockfish-linux-$chesslens_engine_arch-universal.tar.gz" \
  --output "$chesslens_engine_archive"
printf '%s  %s\n' "$chesslens_engine_sha" "$chesslens_engine_archive" | sha256sum --check --status
tar --no-same-owner -xzf "$chesslens_engine_archive" -C "$chesslens_engine_directory"
ln -s "stockfish-linux-$chesslens_engine_arch-universal" "$chesslens_engine_directory/stockfish/stockfish"
chesslens_source_archive="$chesslens_engine_directory/stockfish-sf19-source.tar.gz"
curl --fail --location --proto '=https' --tlsv1.2 --retry 2 --connect-timeout 15 --max-time 180 \
  https://codeload.github.com/official-stockfish/Stockfish/tar.gz/edb0d9db6731067ec50ce619ff372b463bc4dd5d \
  --output "$chesslens_source_archive"
printf '%s  %s\n' 4fc20699a5ecf7883b036ce180020f5e57979623ad719fd2a52839209521581b "$chesslens_source_archive" | sha256sum --check --status
chesslens_materials_directory="$chesslens_engine_directory/stockfish/corresponding-source"
mkdir -- "$chesslens_materials_directory"
tar --no-same-owner -xzf "$chesslens_source_archive" --strip-components=1 -C "$chesslens_materials_directory"
chesslens_network="$chesslens_materials_directory/src/nn-1a298aa575a0.nnue"
curl --fail --location --proto '=https' --tlsv1.2 --retry 2 --connect-timeout 15 --max-time 180 \
  https://data.stockfishchess.org/nn/nn-1a298aa575a0.nnue --output "$chesslens_network"
printf '%s  %s\n' 1a298aa575a085434d29027978dc36867fe9c5bcea9376654b7a8eba1e52dfc2 "$chesslens_network" | sha256sum --check --status
cp -R -- licenses/stockfish "$chesslens_materials_directory/chesslens-notices"
printf '%s  %s\n' "$chesslens_engine_sha" "$(basename -- "$chesslens_engine_archive")" > "$chesslens_engine_directory/SHA256SUMS"
printf '%s  %s\n' 4fc20699a5ecf7883b036ce180020f5e57979623ad719fd2a52839209521581b stockfish-sf19-source.tar.gz >> "$chesslens_engine_directory/SHA256SUMS"
printf '%s  %s\n' 1a298aa575a085434d29027978dc36867fe9c5bcea9376654b7a8eba1e52dfc2 stockfish/corresponding-source/src/nn-1a298aa575a0.nnue >> "$chesslens_engine_directory/SHA256SUMS"
printf 'Stockfish 19 installed: %s/stockfish/stockfish\nOriginal archive, licences, authors, complete source/network and build documentation are retained.\n' "$chesslens_engine_directory"
