#!/usr/bin/env python3
"""Validate all binary and Unity source NuGet packages before publication."""

import pathlib
import sys
import xml.etree.ElementTree as ET
import zipfile

from package_artifacts import verify_packages


if __name__ == "__main__":
    try:
        if len(sys.argv) != 4:
            raise ValueError("Usage: verify-package-contents.py <directory> <version> <commit>")
        verify_packages(pathlib.Path(sys.argv[1]), pathlib.Path(__file__).resolve().parents[1], sys.argv[2], sys.argv[3])
    except (ValueError, OSError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"Package verification failed: {error}", file=sys.stderr)
        sys.exit(1)
