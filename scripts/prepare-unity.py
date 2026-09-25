#!/usr/bin/env python3
"""Create an isolated Unity consumer of the actual NuGet and UPM artifacts."""

import json
import pathlib
import shutil
import sys
import xml.etree.ElementTree as ET


def prepare(source):
    repository = pathlib.Path(__file__).resolve().parents[1]
    version = ET.parse(repository / "Directory.Build.props").findtext(".//Version")
    project = repository / "artifacts/unity-project"
    feed = repository / "artifacts/packages"
    if (project / "Temp/UnityLockfile").exists():
        raise ValueError("Close the generated Unity consumer before preparing it again.")
    names = [f"{name}.{version}.nupkg" for name in ("MackySoft.Navigathena", "MackySoft.Navigathena.MicrosoftDI")]
    names += [f"{directory.name}-{version}.tgz" for directory in sorted((repository / "packages").iterdir()) if directory.is_dir()]
    for name in names:
        if not (source / name).is_file():
            raise ValueError(f"Missing verified artifact: {source / name}")
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
    for name in manifest["dependencies"]:
        if name.startswith("com.mackysoft.navigathena."):
            manifest["dependencies"][name] = f"file:../../packages/{name}-{version}.tgz"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    config_path = project / "Assets/NuGet.config"
    config = ET.parse(config_path)
    config.find("./packageSources/add[@key='local']").set("value", "../../packages")
    ET.SubElement(config.find("config"), "add", key="InstallFromCache", value="false")
    config.write(config_path, encoding="utf-8", xml_declaration=True)
    packages = ET.parse(project / "Assets/packages.config")
    for package in packages.findall("package"):
        if package.get("id").startswith("MackySoft.Navigathena") and package.get("version") != version:
            raise ValueError("Unity packages.config does not match Directory.Build.props.")
    print(f"Unity consumer: {project}")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: prepare-unity.py <verified-package-directory>")
        prepare(pathlib.Path(sys.argv[1]))
    except (ValueError, OSError, ET.ParseError) as error:
        print(f"Unity preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
