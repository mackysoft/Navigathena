#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
run_unity=false
package_directory=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --unity) run_unity=true; shift ;;
    --package-output)
      [[ $# -ge 2 ]] || { printf 'Missing --package-output value.\n' >&2; exit 2; }
      package_directory="$2"; shift 2 ;;
    *) printf 'Usage: %s [--unity] [--package-output <directory>]\n' "$0" >&2; exit 2 ;;
  esac
done

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/navigathena-verify.XXXXXX")"
trap 'rm -rf -- "$temporary_root"' EXIT
if [[ -z "$package_directory" ]]; then
  package_directory="$temporary_root/packages"
fi
mkdir -p "$package_directory"
package_directory="$(cd -- "$package_directory" && pwd -P)"
if compgen -G "$package_directory/*.nupkg" >/dev/null || compgen -G "$package_directory/*.tgz" >/dev/null; then
  printf 'Package output must not contain previous packages: %s\n' "$package_directory" >&2
  exit 1
fi

cd "$repository_root"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
dotnet restore MackySoft.Navigathena.slnx
bash scripts/code-quality.sh verify
dotnet build MackySoft.Navigathena.slnx --configuration Release --no-restore --nologo
dotnet test MackySoft.Navigathena.slnx --configuration Release --no-build --no-restore --nologo
dotnet pack MackySoft.Navigathena.slnx --configuration Release --no-build --no-restore --output "$package_directory" --nologo
python3 scripts/pack-upm.py "$package_directory"
bash scripts/verify-packages.sh "$package_directory"
if [[ "$run_unity" == true ]]; then
  bash scripts/prepare-unity.sh "$package_directory"
  bash scripts/test-unity.sh
fi
printf 'verify: passed\n'
