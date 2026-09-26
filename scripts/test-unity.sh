#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ $# -ne 0 ]]; then
  printf 'Usage: %s\n' "$0" >&2
  exit 2
fi
cd "$repository_root"
mkdir -p artifacts/unity-results
result_directory="$(mktemp -d "$repository_root/artifacts/unity-results/run.XXXXXX")"
unity test artifacts/unity-project --mode PlayMode \
  --output "$result_directory/results.xml" \
  --timeout 600 --format json --non-interactive
python3 scripts/verify-unity-results.py "$result_directory"
