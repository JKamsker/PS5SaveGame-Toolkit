"""Shared scratch and byte-comparison helpers for offline importer checks."""

import os
import tempfile
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parent
PACK = ROOT.parent / "SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog"
GOLDEN = ROOT / "golden/catalog"


def scratch_directory():
    # Honour the review's external scratch policy; never silently write into the repo.
    root = Path(os.environ.get("SAVETOOLKIT_TEST_TEMP", tempfile.gettempdir())).resolve()
    repository = ROOT.parents[2]
    if root == repository or repository in root.parents:
        raise ValueError("Importer test scratch must stay outside the repository")
    root.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(prefix="arise-importer-", dir=root)


def no_network():
    # Reject even accidental urllib/other socket clients, including on cache misses.
    return patch("socket.socket.connect", side_effect=AssertionError("Importer tests must stay offline"))


def pack_bytes(directory, *, canonical_json_newlines=False):
    result = {}
    for path in directory.rglob("*"):
        if path.is_file():
            content = path.read_bytes()
            # Git autocrlf may expand checked-out JSON. Importers and pinned hashes
            # use LF blobs. Only canonicalize expected inputs, never actual output.
            if canonical_json_newlines and path.suffix == ".json":
                content = content.replace(b"\r\n", b"\n")
            result[path.relative_to(directory).as_posix()] = content
    return result


def assert_pack_equal(test, expected, directory):
    actual = pack_bytes(directory)
    test.assertEqual(set(expected), set(actual), "Pack file inventory changed")
    for name, content in expected.items():
        with test.subTest(file=name):
            # Report a readable filename/hash instead of a megabyte-long JSON diff.
            test.assertTrue(content == actual[name], "Pack bytes changed: " + name)
