#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
arguments=(--mode PlayMode --timeout 600 --format json --non-interactive)
if [[ $# -gt 1 || ( $# -eq 1 && "$1" != --documentation ) ]]; then
  printf 'Usage: %s [--documentation]\n' "$0" >&2
  exit 2
fi
cd "$repository_root"
mkdir -p artifacts/unity-results
result_directory="$(mktemp -d "$repository_root/artifacts/unity-results/run.XXXXXX")"
editor_arguments=(-logFile "$result_directory/editor.log")
if [[ $# -eq 1 ]]; then
  editor_arguments+=(-exportDocfxProjects)
fi
unity test artifacts/unity-project --output "$result_directory/results.xml" "${arguments[@]}" -- "${editor_arguments[@]}"
dotnet run --project eng/RepositoryTools --configuration Release -- verify-unity-results "$result_directory"
