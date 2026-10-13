#!/usr/bin/env bash
# Source from a native Linux/WSL terminal; values are read as data, never evaluated.
chesslens_workspace="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "${ConnectionStrings__Database:-}" ]]; then
  chesslens_password=""
  while IFS= read -r chesslens_line || [[ -n "$chesslens_line" ]]; do
    if [[ "$chesslens_line" == POSTGRES_PASSWORD=* ]]; then
      chesslens_password="${chesslens_line#POSTGRES_PASSWORD=}"
      chesslens_password="${chesslens_password%$'\r'}"
      break
    fi
  done < "$chesslens_workspace/.env"
  if [[ -z "$chesslens_password" || "$chesslens_password" == replace-with-a-long-random-password ]]; then
    echo 'Set a random POSTGRES_PASSWORD in .env first.' >&2
    return 1 2>/dev/null || exit 1
  fi
  export ConnectionStrings__Database="Host=localhost;Port=${DATABASE_PORT:-54329};Database=chesslens;Username=chesslens;Password=$chesslens_password;Include Error Detail=false"
  unset chesslens_password
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}"
if [[ -z "${Stockfish__Path:-}" && -x "$chesslens_workspace/.local/stockfish/stockfish-linux-x86-64-universal" ]]; then
  export Stockfish__Path="$chesslens_workspace/.local/stockfish/stockfish-linux-x86-64-universal"
fi
unset chesslens_line chesslens_workspace
