#!/usr/bin/env python3
"""Resume a release only when every existing NuGet payload matches its artifact."""

import argparse
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile

from package_artifacts import PACKAGE_IDS, package_paths, require_same_payload


def download(package, version, destination):
    name = package.lower()
    version = version.lower()
    url = f"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg"
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            destination.write_bytes(response.read())
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return False
        raise
    subprocess.run(["dotnet", "nuget", "verify", str(destination), "--all"], check=True)
    return True


def inspect(artifacts, version, destination, fetch=download):
    missing = []
    for package, artifact in zip(PACKAGE_IDS, package_paths(artifacts, version)):
        published = destination / artifact.name
        if fetch(package, version, published):
            require_same_payload(artifact, published)
        else:
            missing.append(artifact)
    return missing


def prepare(artifacts, version, pending, fetch=download):
    if pending.exists():
        raise ValueError(f"Use a new publication staging directory: {pending}")
    with tempfile.TemporaryDirectory(prefix="navigathena-publication-") as temporary:
        missing = inspect(artifacts, version, pathlib.Path(temporary), fetch)
    # Do not stage any package until every published package has been checked.
    pending.mkdir(parents=True)
    for artifact in missing:
        shutil.copyfile(artifact, pending / artifact.name)
    return bool(missing)


def verify(artifacts, version, destination, fetch=download):
    destination.mkdir(parents=True, exist_ok=False)
    missing = inspect(artifacts, version, destination, fetch)
    if missing:
        raise ValueError(f"Published packages are missing: {[path.name for path in missing]}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("prepare", "verify"))
    parser.add_argument("artifacts", type=pathlib.Path)
    parser.add_argument("version")
    parser.add_argument("destination", type=pathlib.Path)
    args = parser.parse_args()
    if args.mode == "verify":
        verify(args.artifacts, args.version, args.destination)
        return
    required = prepare(args.artifacts, args.version, args.destination)
    print(f"Publication required: {required}")
    if "GITHUB_OUTPUT" in os.environ:
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"publish-required={str(required).lower()}\n")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, urllib.error.URLError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f"Publication verification failed: {error}", file=sys.stderr)
        sys.exit(1)
