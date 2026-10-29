"""Run with python -I -B plugins/EldenRing/Evidence/tests/test_importers.py -v."""

import copy
import json
from pathlib import Path
import socket
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import importer_support as support


class ImporterGoldenTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="eldenring-importers-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        # An accidental future downloader must fail rather than silently turn this into an online test.
        self.network = patch.object(socket, "socket", side_effect=AssertionError("Network forbidden in importer tests"))
        self.network.start()
        self.addCleanup(self.network.stop)

    def assertGolden(self, name, actual):
        expected = json.loads((support.FIXTURES / f"{name}.sha256.json").read_text(encoding="utf-8"))
        self.assertEqual(expected, actual)

    def test_progress_regenerates_every_committed_file(self):
        output = support.progress_pack(self.root)
        shipped = support.PACKS / "eldenring.progress.en"
        self.assertEqual(set(support.snapshot(shipped)), set(support.snapshot(output)))
        for path in output.iterdir():
            with self.subTest(file=path.name):
                # Git's text=auto checks these text files out as CRLF on Windows. Compare the
                # committed LF representation, also used by the F2 pack hash contract.
                self.assertEqual((shipped / path.name).read_bytes().replace(b"\r\n", b"\n"), path.read_bytes())

    def test_name_stage_regenerates_manifest_data_and_sidecars(self):
        output = support.name_pack(self.root)
        self.assertGolden("names", support.snapshot(output))
        # The shipped data/manifest and sidecar footers were later enriched/edited by hand.
        self.assertEqual((support.PACKS / "eldenring.names.en/LICENSE-MIT.txt").read_bytes()
                         .replace(b"\r\n", b"\n"), (output / "LICENSE-MIT.txt").read_bytes())

    def test_community_literal_pipeline_matches_original_bytes(self):
        output = support.community_pack(self.root)
        self.assertGolden("community", support.snapshot(output))
        pack = output / "ReferencePacks/eldenring.community.en"
        flags = json.loads((pack / "flags.json").read_text(encoding="utf-8"))["records"]
        self.assertEqual(5, len(flags))
        repeated = next(row for row in flags if row["id"].startswith("event-flag:76100:"))
        self.assertEqual("2", repeated["attributes"]["sourceOccurrences"])
        self.assertEqual("3262", repeated["attributes"]["offset"])
        self.assertEqual("3", repeated["attributes"]["bit"])
        self.assertNotIn("offset", next(row for row in flags if row["id"].startswith("event-flag:999999:"))["attributes"])

    def test_visual_pipeline_matches_original_bytes_including_images(self):
        import PIL
        from PIL import features
        self.assertEqual("12.2.0", PIL.__version__, "Install tests/requirements.txt; encoders affect golden bytes")
        self.assertEqual("1.6.0", features.version("webp"), "Golden WebP bytes require libwebp 1.6.0")
        output = support.visual_pack(self.root)
        self.assertGolden("visual", support.visual_snapshot(output))
        positions = json.loads((output / "names/map-positions.json").read_text(encoding="utf-8"))["records"]
        self.assertEqual(["100", "101", "102", "103", "107"], [row["id"] for row in positions])
        summary = json.loads((output / "Evidence/visual-summary.json").read_text(encoding="utf-8"))
        self.assertEqual(1, summary["uniqueIcons"])
        self.assertEqual(3, summary["backgrounds"])

    def test_progress_rejects_changed_pinned_input_before_any_output(self):
        module = support.load("import-progress-catalog")
        support.rust_inputs(self.root / "inputs")
        module.OUTPUT = self.root / "output"
        target = self.root / "inputs/progress/graces.rs"
        target.write_bytes(target.read_bytes() + b" ")
        with self.assertRaisesRegex(ValueError, "Source differs from pinned revision: graces.rs"):
            support.run(module, target.parent)
        self.assertFalse(module.OUTPUT.exists())

    def test_names_reject_changed_notice_or_last_table_before_any_output(self):
        module = support.load("import-name-catalog")
        support.rust_inputs(self.root / "inputs")
        module.OUTPUT = self.root / "output"
        for filename in ("LICENSE-MIT", "src_db_aow_name.rs"):
            with self.subTest(file=filename):
                target = self.root / "inputs/names" / filename
                original = target.read_bytes()
                target.write_bytes(original + b" ")
                with self.assertRaises(ValueError):
                    support.run(module, target.parent)
                self.assertFalse(module.OUTPUT.exists())
                target.write_bytes(original)

    def test_community_rejects_unpinned_archive(self):
        module = support.load("import-community-catalog")
        (self.root / "tga.zip").write_bytes(b"not a pinned archive")
        with self.assertRaisesRegex(ValueError, "Unreviewed archive: tga.zip"):
            module.S.Sources(self.root)

    def test_progress_rejects_conflicting_lookups_and_missing_boss_regions(self):
        module = support.load("import-progress-catalog")
        support.rust_inputs(self.root / "inputs")
        texts = {path.name: path.read_text(encoding="utf-8") for path in (self.root / "inputs/progress").iterdir()}
        conflicting = {**texts, "event_flags.rs": "(1,(0x1,0))\n(1,(0x2,0))"}
        with self.assertRaisesRegex(ValueError, "Conflicting event lookup: 1"):
            module.progress(conflicting)
        missing = {**texts, "bosses.rs": 'HashMap::from([(Boss::Test, (1, "Name"))]))',
                   "event_flags.rs": texts["event_flags.rs"] + "\n(1,(0x1,0))"}
        with self.assertRaisesRegex(ValueError, "Boss lacks explicit source region"):
            module.progress(missing)

    def test_visual_selection_guards_remain_explicit(self):
        module = support.load("import-visual-assets")
        row = {"id": 10000, "name": "Blue Cloth Cowl", "icon": 1, "description": ["text"], "category": "Consumable"}
        for source in module.TABLES.values():
            with self.subTest(source=source):
                self.assertTrue(module.selected(row, source))
                self.assertFalse(module.selected({**row, "description": []}, source))
                self.assertFalse(module.selected({**row, "icon": 0}, source))
        self.assertFalse(module.selected({**row, "id": 10001}, "weapons"))
        self.assertFalse(module.selected({**row, "name": "Other"}, "armor"))
        self.assertFalse(module.selected({**row, "category": "Unsupported"}, "goods"))

    def test_visual_rejects_changed_pin_before_replacing_pack(self):
        from synthetic_inputs import visual_inputs
        module = support.load("import-visual-assets")
        module.ROOT = self.root
        module.PACK = support.name_pack(self.root)
        (self.root / "Evidence").mkdir()
        scratch = self.root / "visual-inputs"
        visual_inputs(module, scratch)
        before = support.snapshot(module.PACK)
        (scratch / "goods.ts").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "Source hash mismatch: goods.ts"):
            support.run(module, scratch)
        self.assertEqual(before, support.snapshot(module.PACK))

    def test_visual_image_validation_rejects_signature_and_dimensions(self):
        from PIL import Image
        module = support.load("import-visual-assets")
        target = self.root / "image.webp"
        target.write_bytes(b"not WebP")
        with self.assertRaisesRegex(ValueError, "Not a WebP image"):
            module.image(target)
        Image.new("RGBA", (4097, 1)).save(target, "WEBP", lossless=True)
        with self.assertRaisesRegex(ValueError, "Oversized input"):
            module.image(target)

    def test_compaction_preserves_nonconstant_facts_and_whitespace(self):
        module = support.load("import-community-catalog")
        row = module.S.record("0x40000001", " Seed ", "goods", "alternate", "revision", "hash",
                              {"editorGroups": "Seeds", "editorMembershipOccurrences": "2"}, "uncertainty")
        compacted = module.S.compact_records([copy.deepcopy(row)], "items.json", "default")[0]
        self.assertEqual({"id": "0x40000001", "name": " Seed ", "category": "goods", "source": "alternate",
                          "attributes": {"upstreamName": " Seed ", "editorGroups": "Seeds",
                                         "editorMembershipOccurrences": "2"}}, compacted)


if __name__ == "__main__":
    unittest.main()
