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

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/navigathena-consumer.XXXXXX")"
trap 'rm -rf -- "$temporary_root"' EXIT
cp -R "$repository_root/scripts/package-smoke" "$temporary_root/consumer"
cp "$repository_root/.editorconfig" "$temporary_root/.editorconfig"
export NAVIGATHENA_PACKAGE_DIRECTORY="$package_directory"
export NavigathenaPackageVersion="$package_version"
export NUGET_PACKAGES="$temporary_root/packages"
export NUGET_HTTP_CACHE_PATH="$temporary_root/http-cache"
export NUGET_PLUGINS_CACHE_PATH="$temporary_root/plugins-cache"
export DOTNET_CLI_HOME="$temporary_root/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
dotnet restore "$temporary_root/consumer/PackageSmoke.csproj" --configfile "$temporary_root/consumer/NuGet.config" --no-cache --nologo
dotnet run --project "$temporary_root/consumer/PackageSmoke.csproj" --configuration Release --no-restore
printf 'packages: verified %s\n' "$package_version"
