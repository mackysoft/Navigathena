#!/usr/bin/env python3
"""Require Unity to consume the exact packaged binaries, sources and assets."""

import pathlib
import sys
import xml.etree.ElementTree as ET
import zipfile

from package_artifacts import BINARY_PACKAGES, CONTENT_PREFIX, PACKAGE_IDS


def verify(feed, project):
    packages = ET.parse(project / "Assets/packages.config")
    versions = {item.get("id"): item.get("version") for item in packages.findall("package") if item.get("id") in PACKAGE_IDS}
    if set(versions) != set(PACKAGE_IDS):
        raise ValueError("The Unity consumer must restore all seven Navigathena packages.")
    assets = project / "Assets"
    for name, version in versions.items():
        root = assets / "Packages" / f"{name}.{version}"
        with zipfile.ZipFile(feed / f"{name}.{version}.nupkg") as archive:
            if name in BINARY_PACKAGES:
                binaries = list(assets.rglob(name + ".dll"))
                expected = root / "lib/netstandard2.1" / f"{name}.dll"
                if binaries != [expected] or expected.read_bytes() != archive.read(f"lib/netstandard2.1/{name}.dll"):
                    raise ValueError(f"Missing, duplicate or stale restored assembly: {name}")
            else:
                restored = root / "Sources"
                expected = {entry.removeprefix(CONTENT_PREFIX): archive.read(entry) for entry in archive.namelist() if entry.startswith(CONTENT_PREFIX)}
                actual = {path.relative_to(restored).as_posix(): path.read_bytes() for path in restored.rglob("*") if path.is_file()}
                if not expected or actual != expected:
                    raise ValueError(f"Restored sources, assets or metadata differ from the package: {name}")
                definitions = list(assets.rglob(name + ".asmdef"))
                if definitions != [restored / "Runtime" / f"{name}.asmdef"] or list(assets.rglob(name + ".dll")):
                    raise ValueError(f"Missing or duplicate source assembly: {name}")
        print(f"NuGetForUnity: {name} {version} verified")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 3:
            raise ValueError("Usage: verify-unity-packages.py <package-directory> <unity-project>")
        verify(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
    except (ValueError, OSError, KeyError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"Unity package verification failed: {error}", file=sys.stderr)
        sys.exit(1)
