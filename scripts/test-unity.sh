#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ $# -gt 1 || ( $# -eq 1 && "$1" != "EditMode" && "$1" != "PlayMode" ) ]]; then
  printf 'Usage: %s [EditMode|PlayMode]\n' "$0" >&2
  exit 2
fi
cd "$repository_root"
mode="${1:-PlayMode}"
mkdir -p artifacts/unity-results
result_directory="$(mktemp -d "$repository_root/artifacts/unity-results/run.XXXXXX")"
unity test artifacts/unity-project --mode "$mode" \
  --output "$result_directory/results.xml" \
  --timeout 600 --format json --non-interactive
python3 scripts/verify-unity-results.py "$result_directory" "$mode"
