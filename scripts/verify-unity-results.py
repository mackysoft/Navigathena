#!/usr/bin/env python3
"""A successful Unity process is not sufficient: require the adapter tests to pass."""

import pathlib
import sys
import xml.etree.ElementTree as ET


def verify(directory):
    name = "MackySoft.Navigathena.Unity.Tests"
    assemblies = []
    for path in directory.rglob("*.xml"):
        root = ET.parse(path).getroot()
        assemblies.extend(root.findall(f".//test-suite[@type='Assembly'][@name='{name}.dll']"))
    if len(assemblies) != 1:
        raise ValueError(f"Expected one Navigathena Unity test assembly result; found {len(assemblies)}")
    assembly = assemblies[0]
    if assembly.find("./properties/property[@name='platform'][@value='PlayMode']") is None:
        raise ValueError(f"{name} must execute in PlayMode")
    total = int(assembly.get("total", "0"))
    if total <= 0 or assembly.get("result") != "Passed" or int(assembly.get("passed", "0")) != total:
        raise ValueError("All Navigathena Unity tests must execute and pass, without skipped tests")
    print(f"Unity PlayMode tests: {total} passed")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: verify-unity-results.py <results-directory>")
        verify(pathlib.Path(sys.argv[1]))
    except (ValueError, OSError, ET.ParseError) as error:
        print(f"Unity test verification failed: {error}", file=sys.stderr)
        sys.exit(1)
