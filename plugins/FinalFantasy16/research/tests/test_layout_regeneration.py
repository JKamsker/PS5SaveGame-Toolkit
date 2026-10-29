import contextlib
import hashlib
import io
from pathlib import Path
import shutil
import sys
import unittest
from unittest.mock import patch

from tooling_support import INPUTS, PACKS, checkout_bytes, load_tool, temporary_directory


class LayoutRegenerationTests(unittest.TestCase):
    def test_pinned_layouts_reproduce_all_four_committed_artifacts_and_stdout(self):
        tool = load_tool("build_layout_catalog")
        with temporary_directory() as scratch:
            pack = Path(scratch) / "pack"
            with patch.object(tool, "PACK", pack), \
                    patch.object(sys, "argv", ["build_layout_catalog.py", str(INPUTS / "nex-layouts")]), \
                    contextlib.redirect_stdout(io.StringIO()) as stdout:
                tool.main()
            expected_stdout = []
            for filename, count in (("ffxvi_maps.json", 107), ("ffxvi_summons.json", 10),
                                    ("ffxvi_skills.json", 3), ("pack.json", None)):
                expected = checkout_bytes(PACKS / "ffxvi.nex.en" / filename)
                with self.subTest(file=filename):
                    self.assertEqual(expected, (pack / filename).read_bytes())
                if filename in ("ffxvi_maps.json", "ffxvi_summons.json"):
                    expected_stdout.append(f"{Path(filename).stem}: {count} records, "
                                           f"{hashlib.sha256(expected).hexdigest()}\n")
            self.assertEqual("".join(expected_stdout), stdout.getvalue())
            self.assertEqual(4, len(list(pack.iterdir())))

    def test_each_hash_mismatch_precedes_output_directory_creation(self):
        tool = load_tool("build_layout_catalog")
        for filename in tool.LAYOUT_HASHES:
            with self.subTest(file=filename), temporary_directory() as scratch:
                root = Path(scratch)
                inputs = root / "inputs"
                shutil.copytree(INPUTS / "nex-layouts", inputs)
                (inputs / filename).write_bytes(b"changed input\n")
                pack = root / "pack"
                with patch.object(tool, "PACK", pack), \
                        patch.object(sys, "argv", ["build_layout_catalog.py", str(inputs)]):
                    with self.assertRaises(ValueError) as caught:
                        tool.main()
                self.assertEqual(f"{filename} does not match reviewed revision {tool.REVISION}",
                                 str(caught.exception))
                self.assertFalse(pack.exists())

    def test_skill_annotation_failure_preserves_earlier_catalog_writes(self):
        tool = load_tool("build_layout_catalog")
        with temporary_directory() as scratch:
            root = Path(scratch)
            inputs = root / "inputs"
            shutil.copytree(INPUTS / "nex-layouts", inputs)
            content = b"// - One (skill 39)\n// - Duplicate (skill 39)\n// - Three (skill 49)\n"
            (inputs / "Skill.layout").write_bytes(content)
            hashes = {**tool.LAYOUT_HASHES, "Skill.layout": hashlib.sha256(content).hexdigest()}
            pack = root / "pack"
            with patch.object(tool, "PACK", pack), patch.object(tool, "LAYOUT_HASHES", hashes), \
                    patch.object(sys, "argv", ["build_layout_catalog.py", str(inputs)]), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(ValueError) as caught:
                    tool.main()
            self.assertEqual("Pinned Skill.layout must contain exactly three distinct named skill annotations",
                             str(caught.exception))
            self.assertEqual(["ffxvi_maps.json", "ffxvi_summons.json"], sorted(p.name for p in pack.iterdir()))
