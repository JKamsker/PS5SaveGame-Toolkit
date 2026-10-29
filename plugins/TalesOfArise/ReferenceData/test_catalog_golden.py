"""Byte goldens captured from the unchanged importer using authored pinned inputs."""

import contextlib
import copy
import io
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_catalog
from importer_fixtures import catalog_inputs, write_catalog_inputs
from importer_test_support import GOLDEN, assert_pack_equal, no_network, pack_bytes, scratch_directory


class CatalogGoldenTests(unittest.TestCase):
    def setUp(self):
        self.temporary = self.enterContext(scratch_directory())
        self.scratch = Path(self.temporary)
        self.enterContext(no_network())
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))

    def test_complete_catalog_regeneration_matches_original_bytes_twice(self):
        sources = write_catalog_inputs(self.scratch)
        expected = pack_bytes(GOLDEN, canonical_json_newlines=True)
        self.assertEqual({"pack.json", "source-revisions.json"} |
                         {name + ".json" for name in build_catalog.TITLES}, set(expected))
        destination = self.scratch / "output"
        for _ in range(2):
            build_catalog.build(self.scratch, destination, sources)
            assert_pack_equal(self, expected, destination)

    def test_cached_source_hash_is_checked_before_parsing(self):
        sources = write_catalog_inputs(self.scratch)
        (self.scratch / "0.json").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "Cached response hash mismatch: ToA - Consumables"):
            build_catalog.build(self.scratch, self.scratch / "output", sources)

    def test_cached_source_identity_is_checked_after_hash(self):
        for field, value in [("revid", 999), ("title", "Different title")]:
            with self.subTest(field=field):
                inputs = catalog_inputs()
                source, payload = inputs[0]
                parsed = json.loads(payload)["parse"]
                parsed[field] = value
                payload = json.dumps({"parse": parsed}).encode()
                source["sha256"] = build_catalog.hashlib.sha256(payload).hexdigest()
                inputs[0] = source, payload
                sources = write_catalog_inputs(self.scratch, inputs)
                with self.assertRaisesRegex(ValueError, "Revision/title mismatch"):
                    build_catalog.build(self.scratch, self.scratch / "output", sources)

    def test_missing_published_name_is_rejected(self):
        sources = write_catalog_inputs(self.scratch)
        original = build_catalog.load_source

        def omit_row(source, cache):
            parsed, soup = original(source, cache)
            if source["title"] == "ToA - Consumables":
                soup.select("tr")[-1].decompose()
            return parsed, soup

        with patch.object(build_catalog, "load_source", side_effect=omit_row):
            with self.assertRaisesRegex(ValueError, "Named source rows were omitted:.*Example Tonic"):
                build_catalog.build(self.scratch, self.scratch / "output", sources)

    def test_title_panel_shape_is_rejected(self):
        sources = write_catalog_inputs(self.scratch)
        original = build_catalog.load_source

        def missing_node(source, cache):
            parsed, soup = original(source, cache)
            if source["title"].endswith(": Titles"):
                soup.select("li")[-1].decompose()
            return parsed, soup

        with patch.object(build_catalog, "load_source", side_effect=missing_node):
            with self.assertRaisesRegex(ValueError, "Unexpected skill panel/cost shape"):
                build_catalog.build(self.scratch, self.scratch / "output", sources)

    def test_duplicate_and_colliding_keys_are_rejected_except_repeated_characters(self):
        sources = write_catalog_inputs(self.scratch)
        original = build_catalog.add
        for collision in [False, True]:
            with self.subTest(collision=collision):
                def duplicate(output, dataset, *args, **kwargs):
                    row = original(output, dataset, *args, **kwargs)
                    if dataset == "items":
                        other = copy.deepcopy(row)
                        if collision:
                            other["name"] += " different"
                        output[dataset].append(other)
                    return row

                message = "Catalog key collision" if collision else "Duplicate record requiring explicit reconciliation"
                with patch.object(build_catalog, "add", side_effect=duplicate):
                    with self.assertRaisesRegex(ValueError, message):
                        build_catalog.build(self.scratch, self.scratch / "output", sources)


if __name__ == "__main__":
    unittest.main()
