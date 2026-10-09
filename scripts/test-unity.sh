#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
arguments=(--mode PlayMode --timeout 600 --format json --non-interactive)
editor_arguments=()
if [[ $# -eq 1 && "$1" == --documentation ]]; then
  editor_arguments+=(-exportDocfxProjects)
elif [[ $# -ne 0 ]]; then
  printf 'Usage: %s [--documentation]\n' "$0" >&2
  exit 2
fi
cd "$repository_root"
mkdir -p artifacts/unity-results
result_directory="$(mktemp -d "$repository_root/artifacts/unity-results/run.XXXXXX")"
unity test artifacts/unity-project --output "$result_directory/results.xml" "${arguments[@]}" -- "${editor_arguments[@]}" -logFile "$result_directory/editor.log"
dotnet run --project eng/RepositoryTools --configuration Release -- verify-unity-results "$result_directory" artifacts/unity-project
