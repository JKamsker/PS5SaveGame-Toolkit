"""Boundary and workflow regressions for the C# source-size guard."""

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/check_csharp_size.py"


class SizeGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("check_csharp_size", SCRIPT)
        cls.guard = importlib.util.module_from_spec(spec)
        assert spec.loader is not None
        sys.modules[spec.name] = cls.guard
        spec.loader.exec_module(cls.guard)

    def test_line_count_handles_crlf_and_final_newline(self):
        self.assertEqual(301, self.guard.count_lines(("x\r\n" * 301).encode(), "a.cs"))
        self.assertEqual(2, self.guard.count_lines(b"x\r\ny", "a.cs"))

    def test_name_status_keeps_rename_destination_and_ignores_deletes_by_contract(self):
        records = b"M\0folder/with space.cs\0R100\0old.cs\0new name.cs\0"
        self.assertEqual(["folder/with space.cs", "new name.cs"], self.guard.parse_name_status(records))

    def test_staged_mode_reads_index_and_enforces_boundaries(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            (root / ".csharp-size-policy.json").write_text(json.dumps({
                "warningLines": 300, "errorLines": 500,
                "generatedGlobs": ["generated/**/*.cs"], "legacyExemptions": []
            }), encoding="utf-8")
            source = root / "folder with space" / "over.cs"
            source.parent.mkdir()
            source.write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", ".")
            result = self._run(root, "--staged")
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn("over.cs", result.stdout)

            source.write_text("line\n" * 301, encoding="utf-8")
            self._git(root, "add", ".")
            result = self._run(root, "--staged")
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("WARNING", result.stdout)

    def test_changed_mode_uses_explicit_base_and_generated_and_legacy_policy(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            policy = {
                "warningLines": 300, "errorLines": 500,
                "generatedGlobs": ["generated/**/*.cs"],
                "legacyExemptions": [{"path": "legacy.cs", "maxLines": 700,
                                      "reason": "tracked generator output pending split"}],
            }
            (root / ".csharp-size-policy.json").write_text(json.dumps(policy), encoding="utf-8")
            (root / "legacy.cs").write_text("line\n" * 501, encoding="utf-8")
            (root / "generated").mkdir()
            (root / "generated" / "output.cs").write_text("line\n" * 900, encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "baseline")
            base = self._git(root, "rev-parse", "HEAD").stdout.strip()
            (root / "new name.cs").write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", "new name.cs")
            self._git(root, "commit", "-m", "feature")
            result = self._run(root, "--changed", "--base", base)
            self.assertEqual(1, result.returncode)
            self.assertIn("new name.cs", result.stdout)
            self.assertNotIn("output.cs", result.stdout)

            self._git(root, "rm", "new name.cs")
            (root / "small.cs").write_text("line\n" * 2, encoding="utf-8")
            self._git(root, "add", "small.cs")
            self._git(root, "commit", "-m", "remove")
            result = self._run(root, "--changed", "--base", base)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_missing_base_explains_detached_checkout_recovery(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            (root / ".csharp-size-policy.json").write_text(
                '{"warningLines": 300, "errorLines": 500, "generatedGlobs": [], "legacyExemptions": []}',
                encoding="utf-8")
            result = self._run(root, "--changed", "--base", "missing-base")
            self.assertEqual(2, result.returncode)
            self.assertIn("Fetch the PR base", result.stderr)

    def test_workflow_uses_pr_base_or_origin_main_without_local_main_assumption(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("check_csharp_size.py --changed --base", workflow)
        self.assertIn("refs/remotes/origin/main", workflow)
        self.assertIn("pull_request.base.sha", workflow)

    @staticmethod
    def _git(root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", "-C", str(root), *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def _run(self, root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["python", str(SCRIPT), "--root", str(root), *args],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


if __name__ == "__main__":
    unittest.main()
