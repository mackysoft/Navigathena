#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ $# -ne 1 ]] || [[ "$1" != format && "$1" != verify ]]; then
  printf 'Usage: %s <format|verify>\n' "$0" >&2
  exit 2
fi
arguments=(--verbosity minimal)
if [[ "$1" == verify ]]; then
  arguments+=(--verify-no-changes)
fi
cd "$repository_root"
dotnet format MackySoft.Navigathena.slnx "${arguments[@]}"
dotnet format whitespace --folder --include packages tests/Unity/Assets scripts/package-smoke "${arguments[@]}"
