#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! -d "$1" ]]; then
  printf 'Usage: %s <package-directory>\n' "$0" >&2
  exit 2
fi
package_directory="$(cd -- "$1" && pwd -P)"
repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
package_version="$(dotnet msbuild "$repository_root/src/MackySoft.Navigathena/MackySoft.Navigathena.csproj" -nologo -getProperty:PackageVersion)"
repository_commit="$(git -C "$repository_root" rev-parse --verify HEAD)"
python3 "$repository_root/scripts/verify-package-contents.py" "$package_directory" "$package_version" "$repository_commit"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/navigathena-package-tests.XXXXXX")"
trap 'rm -rf -- "$temporary_root"' EXIT
mkdir -p "$temporary_root/tests"
# Run the same behavior tests against packages, with no library source available.
tar -C "$repository_root/tests/MackySoft.Navigathena.Tests" --exclude='./bin' --exclude='./obj' -cf - . \
  | tar -C "$temporary_root/tests" -xf -
cp "$repository_root/Directory.Build.props" "$repository_root/Directory.Packages.props" \
  "$repository_root/global.json" "$repository_root/.editorconfig" "$temporary_root/"
export NAVIGATHENA_PACKAGE_DIRECTORY="$package_directory"
export NUGET_PACKAGES="$temporary_root/packages"
export NUGET_HTTP_CACHE_PATH="$temporary_root/http-cache"
export NUGET_PLUGINS_CACHE_PATH="$temporary_root/plugins-cache"
export DOTNET_CLI_HOME="$temporary_root/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
test_project="$temporary_root/tests/MackySoft.Navigathena.Tests.csproj"
cd "$temporary_root"
dotnet restore "$test_project" -p:TestPackageVersion="$package_version" \
  --configfile "$repository_root/scripts/package-tests.NuGet.config" --no-cache --nologo
dotnet test "$test_project" -p:TestPackageVersion="$package_version" --configuration Release --no-restore --nologo
printf 'packages: verified %s\n' "$package_version"
