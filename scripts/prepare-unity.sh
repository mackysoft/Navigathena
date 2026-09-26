#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ $# -lt 1 ]]; then
  printf 'Usage: %s <verified-package-directory> [--version <version>] [--upgrade]\n' "$0" >&2
  exit 2
fi
package_directory="$(cd -- "$1" && pwd -P)"
shift
cd "$repository_root"
python3 scripts/prepare-unity.py "$package_directory" "$@"
dotnet tool restore
dotnet tool run nugetforunity restore artifacts/unity-project
python3 scripts/verify-unity-packages.py artifacts/unity-feed artifacts/unity-project
