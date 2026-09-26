#!/usr/bin/env python3
"""Create an isolated Unity consumer of the actual NuGet artifacts."""

import argparse
import json
import pathlib
import shutil
import sys
import xml.etree.ElementTree as ET

from package_artifacts import PACKAGE_IDS, package_paths, release_version


def prepare(source):
    repository = pathlib.Path(__file__).resolve().parents[1]
    version = release_version(repository)
    project = repository / "artifacts/unity-project"
    feed = repository / "artifacts/unity-feed"
    if (project / "Temp/UnityLockfile").exists():
        raise ValueError("Close the generated Unity consumer before preparing it again.")
    names = [path.name for path in package_paths(source, version)]
    feed.mkdir(parents=True, exist_ok=True)
    if source.resolve() != feed.resolve():
        for name in names:
            shutil.copyfile(source / name, feed / name)
    # This dedicated consumer is generated; retain only Library's import cache.
    for directory in ("Assets", "Packages", "ProjectSettings"):
        target = project / directory
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(repository / "tests/Unity" / directory, target)
    manifest_path = project / "Packages/manifest.json"
    manifest = json.loads(manifest_path.read_text())
    if any(name.startswith("com.mackysoft.navigathena") for name in manifest["dependencies"]):
        raise ValueError("Navigathena must be restored from NuGet, not from UPM or repository sources.")
    config_path = project / "Assets/NuGet.config"
    config = ET.parse(config_path)
    config.find("./packageSources/add[@key='local']").set("value", "../../unity-feed")
    if config.find("./config/add[@key='InstallFromCache']") is None:
        ET.SubElement(config.find("config"), "add", key="InstallFromCache", value="false")
    config.write(config_path, encoding="utf-8", xml_declaration=True)
    packages = ET.parse(project / "Assets/packages.config")
    if {package.get("id") for package in packages.findall("package") if package.get("id").startswith("MackySoft.Navigathena")} != set(PACKAGE_IDS):
        raise ValueError("Unity packages.config must exercise every release package.")
    for package in packages.findall("package"):
        if package.get("id").startswith("MackySoft.Navigathena") and package.get("version") != version:
            raise ValueError("Unity packages.config does not match Directory.Build.props.")
    print(f"Unity consumer: {project}")


if __name__ == "__main__":
    try:
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument("source", type=pathlib.Path)
        args = parser.parse_args()
        prepare(args.source)
    except (ValueError, OSError, ET.ParseError) as error:
        print(f"Unity preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
