#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
cd "$repository_root"
if [[ $# -ne 0 ]]; then
  printf 'Usage: %s\n' "$0" >&2
  exit 2
fi

for assembly in src/*/Runtime/*.asmdef; do
  name="$(basename "$assembly" .asmdef)"
  if [[ ! -f "artifacts/documentation/unity/$name/$name.csproj" ]]; then
    printf 'Missing Unity documentation project: %s. Run the Unity documentation export first.\n' "$name" >&2
    exit 1
  fi
done

# Discard generated pages so removed APIs cannot remain in the published site.
rm -rf -- artifacts/documentation/api artifacts/documentation/site

dotnet tool restore
dotnet tool run docfx Documentation/docfx.json --warningsAsErrors
# Keep build metadata containing local paths outside the published site.
mv artifacts/documentation/site/manifest.json artifacts/documentation/manifest.json
