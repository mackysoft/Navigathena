import pathlib
import sys
import tempfile
import unittest
from unittest import mock
import urllib.error
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from package_artifacts import PACKAGE_IDS, require_same_payload
from publication import download, prepare, verify


class PublicationTests(unittest.TestCase):
    version = "2.0.0-preview.1"

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.artifacts = self.root / "artifacts"
        self.artifacts.mkdir()
        self.pending = self.root / "pending"
        for name in PACKAGE_IDS:
            self.write_package(self.artifacts / f"{name}.{self.version}.nupkg", name)

    @staticmethod
    def write_package(path, content, signed=False):
        with zipfile.ZipFile(path, "w") as package:
            package.writestr("package.nuspec", content)
            package.writestr("lib/netstandard2.1/Library.dll", "binary")
            package.writestr("[Content_Types].xml", "signed" if signed else "unsigned")
            if signed:
                package.writestr(".signature.p7s", "repository signature")

    def fetch(self, present):
        def download(name, version, destination):
            self.assertEqual(self.version, version)
            if name not in present:
                return False
            self.write_package(destination, name, signed=True)
            return True
        return download

    def test_new_release_stages_all_packages(self):
        self.assertTrue(prepare(self.artifacts, self.version, self.pending, self.fetch(())))
        self.assertEqual({path.name for path in self.artifacts.iterdir()}, {path.name for path in self.pending.iterdir()})

    def test_complete_release_does_not_republish_signed_packages(self):
        self.assertFalse(prepare(self.artifacts, self.version, self.pending, self.fetch(PACKAGE_IDS)))
        self.assertEqual([], list(self.pending.iterdir()))

    def test_partial_release_stages_only_missing_packages(self):
        self.assertTrue(prepare(self.artifacts, self.version, self.pending, self.fetch(PACKAGE_IDS[:3])))
        self.assertEqual({f"{name}.{self.version}.nupkg" for name in PACKAGE_IDS[3:]}, {path.name for path in self.pending.iterdir()})
        for path in self.pending.iterdir():
            require_same_payload(self.artifacts / path.name, path)

    def test_different_published_payload_stops_before_staging(self):
        def download(name, version, destination):
            if name != PACKAGE_IDS[-1]:
                return False
            self.write_package(destination, "different dependency manifest", signed=True)
            return True
        with self.assertRaisesRegex(ValueError, "differs"):
            prepare(self.artifacts, self.version, self.pending, download)
        self.assertFalse(self.pending.exists())

    def test_transport_failure_does_not_count_as_missing_package(self):
        for status in (401, 403, 429, 500):
            with self.subTest(status=status):
                def download(name, version, destination):
                    raise urllib.error.HTTPError("https://example.invalid", status, "failure", None, None)
                with self.assertRaises(urllib.error.HTTPError):
                    prepare(self.artifacts, self.version, self.pending, download)
                self.assertFalse(self.pending.exists())

    def test_staging_directory_cannot_retain_packages_from_another_run(self):
        self.pending.mkdir()
        with self.assertRaisesRegex(ValueError, "new publication staging directory"):
            prepare(self.artifacts, self.version, self.pending, self.fetch(()))

    def test_postpublication_requires_every_package(self):
        with self.assertRaisesRegex(ValueError, "missing"):
            verify(self.artifacts, self.version, self.root / "published", self.fetch(PACKAGE_IDS[:3]))

    def test_postpublication_downloads_and_compares_all_packages(self):
        published = self.root / "published"
        verify(self.artifacts, self.version, published, self.fetch(PACKAGE_IDS))
        self.assertEqual(7, len(list(published.iterdir())))

    def test_only_http_not_found_is_an_unpublished_package(self):
        destination = self.root / "download.nupkg"
        for status in (404, 401, 403, 429, 500):
            with self.subTest(status=status):
                failure = urllib.error.HTTPError("https://example.invalid", status, "failure", None, None)
                with mock.patch("publication.urllib.request.urlopen", side_effect=failure):
                    if status == 404:
                        self.assertFalse(download(PACKAGE_IDS[0], self.version, destination))
                    else:
                        with self.assertRaises(urllib.error.HTTPError):
                            download(PACKAGE_IDS[0], self.version, destination)
                self.assertFalse(destination.exists())


if __name__ == "__main__":
    unittest.main()
