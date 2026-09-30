#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ $# -ne 1 ]]; then
  printf 'Usage: %s <version>\n' "$0" >&2
  exit 2
fi
dotnet run --project "$repository_root/eng/RepositoryTools" --configuration Release -- set-version "$repository_root" "$1"
