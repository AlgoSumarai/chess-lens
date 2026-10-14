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
printf '%s  %s\n' "$chesslens_engine_sha" "$(basename -- "$chesslens_engine_archive")" > "$chesslens_engine_directory/SHA256SUMS"
printf 'Stockfish 19 installed: %s/stockfish/stockfish\nOriginal archive, licence, authors, source and build documentation are retained.\n' "$chesslens_engine_directory"
