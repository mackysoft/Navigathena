#!/usr/bin/env python3
"""Validate the public NuGet artifacts before testing or publishing them."""

import pathlib
import sys
import xml.etree.ElementTree as ET
import zipfile


def verify(directory, version, commit):
    repository = pathlib.Path(__file__).resolve().parents[1]
    versions = ET.parse(repository / "Directory.Packages.props")
    dependency = versions.find(".//PackageVersion[@Include='Microsoft.Extensions.DependencyInjection']")
    expected = {
        "MackySoft.Navigathena": {},
        "MackySoft.Navigathena.MicrosoftDI": {
            "MackySoft.Navigathena": version,
            "Microsoft.Extensions.DependencyInjection": dependency.get("Version"),
        },
    }
    if {path.name for path in directory.glob("*.nupkg")} != {f"{name}.{version}.nupkg" for name in expected}:
        raise ValueError("Expected exactly the core and Microsoft DI packages at the configured version.")
    for name, dependencies in expected.items():
        with zipfile.ZipFile(directory / f"{name}.{version}.nupkg") as package:
            required = {
                f"{name}.nuspec", "README.md", "LICENSE",
                f"lib/netstandard2.1/{name}.dll", f"lib/netstandard2.1/{name}.xml",
            }
            if not required.issubset(package.namelist()):
                raise ValueError(f"{name}: missing contents {required - set(package.namelist())}")
            metadata = ET.fromstring(package.read(f"{name}.nuspec")).find("{*}metadata")
            if metadata is None:
                raise ValueError(f"{name}: missing metadata")
            for field, value in {"id": name, "version": version, "license": "MIT", "readme": "README.md"}.items():
                if metadata.findtext(f"{{*}}{field}") != value:
                    raise ValueError(f"{name}: unexpected {field}")
            source = metadata.find("{*}repository")
            if source is None or source.get("url") != "https://github.com/mackysoft/Navigathena" or source.get("type") != "git":
                raise ValueError(f"{name}: incorrect repository metadata")
            if source.get("commit") != commit:
                raise ValueError(f"{name}: repository commit does not match {commit}")
            actual = {item.get("id"): item.get("version") for item in metadata.findall(".//{*}dependency")}
            if actual != dependencies:
                raise ValueError(f"{name}: expected dependencies {dependencies}, found {actual}")
            libraries = [item for item in package.namelist() if item.endswith(".dll")]
            if libraries != [f"lib/netstandard2.1/{name}.dll"]:
                raise ValueError(f"{name}: contains unexpected assemblies {libraries}")
            if "MIT License" not in package.read("LICENSE").decode("utf-8"):
                raise ValueError(f"{name}: missing MIT license text")
        print(f"package: {name} {version}")


if __name__ == "__main__":
    try:
        if len(sys.argv) != 4:
            raise ValueError("Usage: verify-package-contents.py <directory> <version> <commit>")
        verify(pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3])
    except (ValueError, OSError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"Package verification failed: {error}", file=sys.stderr)
        sys.exit(1)
