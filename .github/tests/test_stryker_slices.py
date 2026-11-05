"""Stable slicing regressions, using production inventories as well as synthetic paths."""

from collections import Counter
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / ".github/scripts/stryker_slices.py"
spec = importlib.util.spec_from_file_location("stryker_slices", SCRIPT)
slices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(slices)


class SliceTests(unittest.TestCase):
    def test_deterministic_independent_of_order_and_additions(self):
        files = [f"Folder/File{index}.cs" for index in range(1000)]
        original = {path: slices.slice_index(path) for path in files}
        self.assertEqual(original, {path: slices.slice_index(path) for path in reversed(files)})
        extended = {path: slices.slice_index(path) for path in ["New.cs", *files]}
        self.assertTrue(all(extended[path] == index for path, index in original.items()))
        self.assertEqual(1, slices.slice_index("ActionPlans.cs"))
        partitions = [slices.select_files(["New.cs", *files], str(index)) for index in range(1, 10)]
        self.assertEqual(Counter(["New.cs", *files]), Counter(path for group in partitions for path in group))

    def test_every_file_exactly_once_and_balanced(self):
        for project in ("Core", "Sdk"):
            files = slices.source_files(ROOT / "src" / f"SaveToolkit.{project}")
            partitions = [slices.select_files(files, str(index)) for index in range(1, 10)]
            self.assertEqual(Counter(files), Counter(path for group in partitions for path in group))
            # Hash buckets are statistically balanced, not equal-sized chunks.
            self.assertLessEqual(max(map(len, partitions)), 2 * len(files) / 9)
            self.assertTrue(all(partitions))
            self.assertEqual(files, slices.select_files(files, "all"))

    def test_utc_date_rotation_and_override(self):
        selections = []
        for day in range(1, 10):
            now = datetime(2026, 11, day, tzinfo=timezone.utc)
            selections.append(slices.resolve_slice("", now))
            self.assertEqual("all", slices.resolve_slice("all", now))
        self.assertEqual(set(map(str, range(1, 10))), set(selections))

    def test_invalid_selection_rejected(self):
        for value in ("0", "10", "-1", "ALL", "1,2", "1.0"):
            with self.assertRaises(ValueError):
                slices.select_files(["A.cs"], value)

    def test_inventory_excludes_build_outputs(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ("A.cs", "nested/B.cs", "obj/Generated.cs", "nested/bin/Output.cs"):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()
            self.assertEqual(["A.cs", "nested/B.cs"], slices.source_files(root))

    def test_cli_writes_exact_selection_and_preserves_policy(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "github-output"
            subprocess.run([sys.executable, "-B", str(SCRIPT), "--project", "Sdk", "--slice", "6",
                            "--root", str(ROOT), "--scratch", temp], check=True, capture_output=True,
                           env={**os.environ, "GITHUB_OUTPUT": str(output)})
            config = json.loads((Path(temp) / "stryker-config.json").read_text())["stryker-config"]
            manifest = json.loads((Path(temp) / "slice.json").read_text())
            self.assertEqual(0, config["thresholds"]["break"])
            self.assertEqual("mtp", config["test-runner"])
            self.assertEqual(manifest["files"], config["mutate"])
            self.assertTrue(all(slices.slice_index(path) == 6 for path in manifest["files"]))
            self.assertEqual(f"slice=6\ncount=9\nfiles={len(manifest['files'])}\n", output.read_text())

    def test_empty_inventory_fails_without_writing_a_config(self):
        with tempfile.TemporaryDirectory() as temp:
            result = subprocess.run([sys.executable, "-B", str(SCRIPT), "--project", "Sdk",
                                     "--root", temp, "--scratch", temp], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("no source files", result.stderr)
            self.assertFalse((Path(temp) / "stryker-config.json").exists())


if __name__ == "__main__":
    unittest.main()
