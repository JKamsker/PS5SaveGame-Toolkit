"""Exercise CI base resolution against real, isolated Git remotes."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/resolve_ci_base.py"


class CiBaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.remote = self.root / "remote"
        self.remote.mkdir()
        self.git(self.remote, "init", "-b", "main")
        self.git(self.remote, "config", "user.email", "ci@example.invalid")
        self.git(self.remote, "config", "user.name", "CI test")
        self.git(self.remote, "commit", "--allow-empty", "-m", "baseline")
        self.base = self.git(self.remote, "rev-parse", "HEAD").stdout.strip()
        self.checkout = self.root / "checkout"
        self.git(self.root, "clone", str(self.remote), str(self.checkout))
        self.output = self.root / "github-env"

    def git(self, cwd, *args):
        return subprocess.run(["git", "-C", str(cwd), *args], capture_output=True,
                              text=True, check=True)

    def resolve(self, event="push", before="", pr_base="", ref="refs/heads/dependabot/test"):
        self.output.write_text("", encoding="utf-8")
        return subprocess.run([sys.executable, "-B", str(SCRIPT)], cwd=self.checkout,
                              env={**os.environ, "EVENT_NAME": event, "EVENT_BEFORE": before,
                                   "PR_BASE_SHA": pr_base, "GITHUB_REF": ref,
                                   "GITHUB_ENV": str(self.output)},
                              capture_output=True, text=True)

    def test_force_push_with_unfetchable_before_checks_main(self):
        result = self.resolve(before="1" * 40)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"CSHARP_SIZE_BASE={self.base}", self.output.read_text())
        self.assertIn("rewritten branch", result.stdout)

    def test_missing_but_fetchable_exact_base_is_recovered(self):
        self.git(self.remote, "commit", "--allow-empty", "-m", "new base")
        base = self.git(self.remote, "rev-parse", "HEAD").stdout.strip()
        result = self.resolve(event="pull_request", pr_base=base)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"CSHARP_SIZE_BASE={base}", self.output.read_text())

    def test_present_before_is_preserved(self):
        result = self.resolve(before=self.base)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"CSHARP_SIZE_BASE={self.base}", self.output.read_text())

    def test_protected_and_pr_missing_bases_fail_closed(self):
        for event, ref in (("push", "refs/heads/main"), ("push", "refs/tags/v1.0.0"),
                           ("pull_request", "refs/pull/44/merge")):
            with self.subTest(event=event, ref=ref):
                result = self.resolve(event, "1" * 40, "1" * 40, ref)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", self.output.read_text())

    def test_new_branch_and_manual_run_use_main(self):
        for event, before in (("push", "0" * 40), ("workflow_dispatch", "")):
            with self.subTest(event=event):
                result = self.resolve(event, before)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertIn(f"CSHARP_SIZE_BASE={self.base}", self.output.read_text())

    def test_unavailable_remote_does_not_produce_a_base(self):
        self.git(self.checkout, "remote", "set-url", "origin", str(self.root / "missing"))
        result = self.resolve(before="1" * 40)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("", self.output.read_text())


if __name__ == "__main__":
    unittest.main()
