#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ $# -lt 1 || $# -gt 2 ]]; then
  printf 'Usage: %s <verified-package-directory> [unity-6|unity-2021|unity-2022|unity-2023|unity-6-input-system|unity-2021-input-system]\n' "$0" >&2
  exit 2
fi
package_directory="$(cd -- "$1" && pwd -P)"
unity_configuration="${2:-unity-6}"
case "$unity_configuration" in
  unity-6|unity-2021|unity-2022|unity-2023|unity-6-input-system|unity-2021-input-system) ;;
  *) printf 'Unknown Unity configuration: %s\n' "$unity_configuration" >&2; exit 2 ;;
esac
cd "$repository_root"
project_directory="$repository_root/artifacts/unity-project"
feed_directory="$repository_root/artifacts/unity-feed"
if [[ -f "$project_directory/Temp/UnityLockfile" ]]; then
  printf 'Close the generated Unity consumer before preparing it again.\n' >&2
  exit 1
fi
version="$(dotnet msbuild src/MackySoft.Navigathena/MackySoft.Navigathena.csproj -nologo -getProperty:PackageVersion)"
package_paths="$(dotnet run --project eng/RepositoryTools --configuration Release -- package-paths "$package_directory" "$version")"
mkdir -p "$feed_directory" "$project_directory"
while IFS= read -r package; do
  if [[ "$package_directory" != "$feed_directory" ]]; then
    cp "$package" "$feed_directory/"
  fi
done <<< "$package_paths"
# Recreate this isolated consumer while retaining Unity's import cache.
for directory in Assets Packages ProjectSettings; do
  rm -rf -- "$project_directory/$directory"
  cp -R "tests/Unity/$directory" "$project_directory/$directory"
done
dotnet run --project eng/RepositoryTools --configuration Release -- configure-unity "$project_directory" "$version" "$repository_root/tests/Unity/Configurations/$unity_configuration.json"
printf 'Unity consumer: %s (%s)\n' "$project_directory" "$unity_configuration"
dotnet tool restore
dotnet tool run nugetforunity restore artifacts/unity-project
dotnet run --project eng/RepositoryTools --configuration Release -- verify-unity-packages "$feed_directory" "$project_directory"
