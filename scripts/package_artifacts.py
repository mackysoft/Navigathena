"""The release package set and checks shared by verification and publication."""

import re
import xml.etree.ElementTree as ET
import zipfile


BINARY_PACKAGES = ("MackySoft.Navigathena", "MackySoft.Navigathena.MicrosoftDI")
SOURCE_PACKAGES = {
    "MackySoft.Navigathena.Unity": "MackySoft.Navigathena",
    "MackySoft.Navigathena.Unity.UGUI": "MackySoft.Navigathena.Unity",
    "MackySoft.Navigathena.Unity.UIToolkit": "MackySoft.Navigathena.Unity",
    "MackySoft.Navigathena.Unity.Addressables": "MackySoft.Navigathena.Unity",
    "MackySoft.Navigathena.VContainer": "MackySoft.Navigathena",
}
PACKAGE_IDS = (*BINARY_PACKAGES, *SOURCE_PACKAGES)
CONTENT_PREFIX = "contentFiles/any/any/"


def release_version(repository):
    return ET.parse(repository / "Directory.Build.props").findtext(".//Version")


def package_paths(directory, version):
    expected = {f"{name}.{version}.nupkg" for name in PACKAGE_IDS}
    actual = {path.name for path in directory.glob("*.nupkg")}
    if actual != expected:
        raise ValueError(f"Expected the seven release packages. Missing: {expected - actual}; unexpected: {actual - expected}")
    return [directory / f"{name}.{version}.nupkg" for name in PACKAGE_IDS]


def source_assets(repository, name):
    root = repository / "src" / name
    paths = [root / "Runtime.meta", *(path for path in (root / "Runtime").rglob("*") if path.is_file())]
    assets = {}
    for path in paths:
        if path.is_symlink() or path.suffix not in {".cs", ".asmdef", ".uss", ".meta"}:
            raise ValueError(f"Unexpected source package asset: {path}")
        if path.suffix != ".meta" and not path.with_name(path.name + ".meta").is_file():
            raise ValueError(f"Unity asset metadata missing: {path}")
        assets[CONTENT_PREFIX + path.relative_to(root).as_posix()] = path.read_bytes()
    return assets


def verify_package(path, repository, version, commit):
    with zipfile.ZipFile(path) as package:
        names = package.namelist()
        if len(names) != len(set(names)):
            raise ValueError(f"{path.name}: duplicate archive entries")
        nuspecs = [name for name in names if name.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError(f"{path.name}: expected one package manifest")
        metadata = ET.fromstring(package.read(nuspecs[0])).find("{*}metadata")
        if metadata is None:
            raise ValueError(f"{path.name}: missing metadata")
        name = metadata.findtext("{*}id")
        if name not in PACKAGE_IDS or path.name != f"{name}.{version}.nupkg":
            raise ValueError(f"{path.name}: unexpected package identity")
        for field, value in {"version": version, "license": "MIT", "readme": "README.md"}.items():
            if metadata.findtext(f"{{*}}{field}") != value:
                raise ValueError(f"{name}: unexpected {field}")
        source = metadata.find("{*}repository")
        if source is None or source.get("url") != "https://github.com/mackysoft/Navigathena" or source.get("type") != "git":
            raise ValueError(f"{name}: incorrect repository metadata")
        if source.get("commit") != commit:
            raise ValueError(f"{name}: repository commit does not match {commit}")
        expected_dependencies = {}
        if name in SOURCE_PACKAGES:
            expected_dependencies[SOURCE_PACKAGES[name]] = version
        elif name == "MackySoft.Navigathena.MicrosoftDI":
            versions = ET.parse(repository / "Directory.Packages.props")
            dependency = versions.find(".//PackageVersion[@Include='Microsoft.Extensions.DependencyInjection']")
            expected_dependencies = {
                "MackySoft.Navigathena": version,
                "Microsoft.Extensions.DependencyInjection": dependency.get("Version"),
            }
        dependencies = {item.get("id"): item.get("version") for item in metadata.findall(".//{*}dependency")}
        if dependencies != expected_dependencies:
            raise ValueError(f"{name}: expected dependencies {expected_dependencies}, found {dependencies}")
        required = {f"{name}.nuspec", "README.md", "LICENSE"}
        libraries = {entry for entry in names if entry.startswith("lib/")}
        if name in SOURCE_PACKAGES:
            if libraries != {"lib/netstandard2.1/_._"} or package.read("lib/netstandard2.1/_._"):
                raise ValueError(f"{name}: source packages must not embed assemblies")
            expected = source_assets(repository, name)
            actual = {entry: package.read(entry) for entry in names if entry.startswith("contentFiles/")}
            if actual != expected:
                raise ValueError(f"{name}: packaged sources or assets differ from the release source")
            rules = metadata.findall("{*}contentFiles/{*}files")
            if {rule.get("include") for rule in rules} != {entry.removeprefix("contentFiles/") for entry in expected}:
                raise ValueError(f"{name}: contentFiles do not declare all Unity assets")
            if any(rule.get("buildAction") != "None" for rule in rules):
                raise ValueError(f"{name}: Unity must compile its sources, not the .NET consumer")
        else:
            expected_libraries = {f"lib/netstandard2.1/{name}.dll", f"lib/netstandard2.1/{name}.xml"}
            if libraries != expected_libraries or any(entry.startswith("contentFiles/") for entry in names):
                raise ValueError(f"{name}: unexpected binary package contents")
        if not required.issubset(names) or "MIT License" not in package.read("LICENSE").decode("utf-8"):
            raise ValueError(f"{name}: missing readme, manifest or MIT license")
        allowed = required | libraries
        allowed.update(entry for entry in names if entry.startswith(CONTENT_PREFIX))
        unexpected = set(payload(package)) - allowed
        if unexpected:
            raise ValueError(f"{name}: unexpected package payload {unexpected}")
        return name


def verify_packages(directory, repository, version, commit):
    guids = set()
    for path in package_paths(directory, version):
        name = verify_package(path, repository, version, commit)
        if name in SOURCE_PACKAGES:
            for entry, data in source_assets(repository, name).items():
                if not entry.endswith(".meta"):
                    continue
                match = re.search(rb"^guid: ([0-9a-f]{32})$", data, re.MULTILINE)
                if match is None or match[1] in guids:
                    raise ValueError(f"Missing or duplicate Unity asset GUID: {name}/{entry}")
                guids.add(match[1])
        print(f"package: {name} {version}")


def payload(package):
    # NuGet signing and ZIP container metadata can differ after publication.
    # Compare every delivered file, including the dependency manifest.
    return {
        name: package.read(name)
        for name in package.namelist()
        if name not in {".signature.p7s", "[Content_Types].xml"}
        and not name.startswith(("_rels/", "package/services/metadata/core-properties/"))
        and not name.endswith("/")
    }


def require_same_payload(expected, actual):
    with zipfile.ZipFile(expected) as original, zipfile.ZipFile(actual) as published:
        if payload(original) != payload(published):
            raise ValueError(f"Published package differs from the verified artifact: {expected.name}")
