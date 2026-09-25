#!/usr/bin/env python3
"""Package Unity adapters without embedding NuGet binaries or Unity test projects."""

import json
import pathlib
import sys
import tarfile
import xml.etree.ElementTree as ET


def pack(output):
    repository = pathlib.Path(__file__).resolve().parents[1]
    version = ET.parse(repository / "Directory.Build.props").findtext(".//Version")
    output.mkdir(parents=True, exist_ok=True)
    manifests = sorted((repository / "packages").glob("*/package.json"))
    if len(manifests) != 5:
        raise ValueError("Expected the five Unity and VContainer adapter packages.")
    for manifest in manifests:
        definition = json.loads(manifest.read_text())
        name = definition["name"]
        if name != manifest.parent.name or definition["version"] != version:
            raise ValueError(f"{manifest}: name or version does not match the release")
        for dependency, required_version in definition.get("dependencies", {}).items():
            if dependency.startswith("com.mackysoft.navigathena.") and required_version != version:
                raise ValueError(f"{name}: adapter versions must agree")
        files = sorted(path for path in manifest.parent.rglob("*") if path.is_file())
        for path in files:
            if path.is_symlink() or path.suffix in {".dll", ".csproj", ".nupkg"}:
                raise ValueError(f"Unexpected UPM payload: {path}")
            if path.suffix != ".meta" and not path.with_name(path.name + ".meta").is_file():
                raise ValueError(f"Unity asset metadata missing: {path}")
        for required in ("README.md", "LICENSE.md"):
            if not (manifest.parent / required).is_file():
                raise ValueError(f"{name}: {required} is missing")
        destination = output / f"{name}-{version}.tgz"
        if destination.exists():
            raise ValueError(f"Refusing to overwrite {destination}")
        with tarfile.open(destination, "w:gz") as archive:
            for path in files:
                archive.add(path, arcname="package/" + path.relative_to(manifest.parent).as_posix(), recursive=False)
        with tarfile.open(destination, "r:gz") as archive:
            if archive.extractfile("package/package.json").read() != manifest.read_bytes():
                raise ValueError(f"{name}: packaged manifest differs from its source")
        print(f"upm: {name} {version}")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: pack-upm.py <output-directory>")
        pack(pathlib.Path(sys.argv[1]))
    except (ValueError, OSError, KeyError, ET.ParseError, tarfile.TarError) as error:
        print(f"UPM packaging failed: {error}", file=sys.stderr)
        sys.exit(1)
