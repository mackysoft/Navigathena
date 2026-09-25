#!/usr/bin/env python3
"""Update the release version before review, never during publication."""

import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET


def update(version):
    if not re.fullmatch(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?", version):
        raise ValueError("Use a SemVer version without a v prefix or build metadata")
    for identifier in version.partition("-")[2].split("."):
        if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
            raise ValueError("Numeric prerelease identifiers cannot have leading zeroes")
    root = pathlib.Path(__file__).resolve().parents[1]
    props = root / "Directory.Build.props"
    props.write_text(re.sub(r"<Version>[^<]+</Version>", f"<Version>{version}</Version>", props.read_text(), count=1))
    for path in sorted((root / "packages").glob("*/package.json")):
        manifest = json.loads(path.read_text())
        manifest["version"] = version
        for dependency in manifest.get("dependencies", {}):
            if dependency.startswith("com.mackysoft.navigathena."):
                manifest["dependencies"][dependency] = version
        path.write_text(json.dumps(manifest, indent=2) + "\n")
    config_path = root / "tests/Unity/Assets/packages.config"
    config = ET.parse(config_path)
    for package in config.findall("package"):
        if package.get("id").startswith("MackySoft.Navigathena"):
            package.set("version", version)
    ET.indent(config, space="  ")
    config.write(config_path, encoding="utf-8", xml_declaration=True)
    with config_path.open("a") as stream:
        stream.write("\n")
    print(f"Release version: {version}")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: set-version.py <version>")
        update(sys.argv[1])
    except (ValueError, OSError, ET.ParseError) as error:
        print(f"Version update failed: {error}", file=sys.stderr)
        sys.exit(1)
