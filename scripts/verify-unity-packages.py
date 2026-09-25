#!/usr/bin/env python3
"""Reject stale, missing, or duplicate Navigathena DLLs after NuGetForUnity restore."""

import pathlib
import sys
import xml.etree.ElementTree as ET
import zipfile


def verify(feed, project):
    packages = ET.parse(project / "Assets/packages.config")
    for package in packages.findall("package"):
        name = package.get("id")
        if not name.startswith("MackySoft.Navigathena"):
            continue
        version = package.get("version")
        binaries = list((project / "Assets/Packages").rglob(name + ".dll"))
        if len(binaries) != 1:
            raise ValueError(f"Expected one restored {name}.dll; found {len(binaries)}")
        with zipfile.ZipFile(feed / f"{name}.{version}.nupkg") as archive:
            expected = archive.read(f"lib/netstandard2.1/{name}.dll")
        if binaries[0].read_bytes() != expected:
            raise ValueError(f"Restored {name}.dll differs from the verified release artifact")
        print(f"NuGetForUnity: {name} {version} verified")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 3:
            raise ValueError("Usage: verify-unity-packages.py <package-directory> <unity-project>")
        verify(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
    except (ValueError, OSError, KeyError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"Unity package verification failed: {error}", file=sys.stderr)
        sys.exit(1)
