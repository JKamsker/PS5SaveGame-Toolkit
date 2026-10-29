"""Boundary and workflow regressions for the C# source-size guard."""

import importlib.util
import json
import os
from pathlib import Path
import shutil
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

    def test_generated_directory_pattern_matches_direct_and_nested_children(self):
        policy = self.guard.Policy(300, 500, ("generated/**/*.cs",), {})
        self.assertTrue(self.guard.is_generated("generated/output.cs", policy))
        self.assertTrue(self.guard.is_generated("generated/sub/output.cs", policy))
        self.assertFalse(self.guard.is_generated("src/output.cs", policy))

    def test_policy_rejects_root_wide_and_extension_only_generated_patterns(self):
        for pattern in ("*.cs", "**/*.cs", "src/**/*.cs", "/Generated/**/*.cs", "../Generated/**/*.cs"):
            with self.subTest(pattern=pattern):
                with self.assertRaises(self.guard.GuardError):
                    self.guard.load_policy_bytes(json.dumps({
                        "generatedGlobs": [pattern], "legacyExemptions": []
                    }).encode(), "test policy")

    def test_permissive_global_limits_fail_staged_and_committed_modes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            policy = root / ".csharp-size-policy.json"
            strict = {"warningLines": 300, "errorLines": 500,
                      "generatedGlobs": [], "legacyExemptions": []}
            policy.write_text(json.dumps(strict), encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "strict baseline")
            base = self._git(root, "rev-parse", "HEAD").stdout.strip()
            policy.write_text(json.dumps({"warningLines": 1000, "errorLines": 5000,
                                          "generatedGlobs": [], "legacyExemptions": []}), encoding="utf-8")
            source = root / "TooLarge.cs"
            source.write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", ".")
            staged = self._run(root, "--staged")
            self.assertEqual(2, staged.returncode)
            self.assertIn("warningLines must be <= 300", staged.stderr)
            self._git(root, "commit", "-m", "permissive policy")
            changed = self._run(root, "--changed", "--base", base)
            audited = self._run(root, "--all")
            self.assertEqual(2, changed.returncode)
            self.assertEqual(2, audited.returncode)

    def test_strict_policy_keeps_301_line_warning(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            policy = root / ".csharp-size-policy.json"
            policy.write_text(json.dumps({"warningLines": 300, "errorLines": 500,
                                          "generatedGlobs": [], "legacyExemptions": []}), encoding="utf-8")
            source = root / "301.cs"
            source.write_text("line\n" * 301, encoding="utf-8")
            self._git(root, "add", ".")
            result = self._run(root, "--staged")
            self.assertEqual(0, result.returncode)
            self.assertIn("WARNING 301.cs: 301 lines exceeds 300", result.stdout)

    def test_committed_modes_keep_newline_filename_blob_identity(self):
        if os.name == "nt":
            self.skipTest("POSIX permits newline filenames used by this identity regression")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            policy = root / ".csharp-size-policy.json"
            policy.write_text(json.dumps({"warningLines": 300, "errorLines": 500,
                                          "generatedGlobs": [], "legacyExemptions": []}), encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "baseline")
            base = self._git(root, "rev-parse", "HEAD").stdout.strip()
            source = root / "odd\nname.cs"
            source.write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "newline source")
            all_result = self._run(root, "--all")
            changed_result = self._run(root, "--changed", "--base", base)
            self.assertEqual(1, all_result.returncode, all_result.stdout + all_result.stderr)
            self.assertEqual(1, changed_result.returncode, changed_result.stdout + changed_result.stderr)
            self.assertIn("hard limit 500", all_result.stdout)
            self.assertIn("hard limit 500", changed_result.stdout)

    def test_all_changed_and_staged_preserve_literal_backslash_path_identity(self):
        if os.name == "nt":
            self.skipTest("POSIX permits literal backslash filenames used by this identity regression")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            policy = root / ".csharp-size-policy.json"
            policy.write_text(json.dumps({"warningLines": 300, "errorLines": 500,
                                          "generatedGlobs": [], "legacyExemptions": []}), encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "baseline")
            base = self._git(root, "rev-parse", "HEAD").stdout.strip()
            literal_backslash = root / "a\\b.cs"
            slash_path = root / "a" / "b.cs"
            slash_path.parent.mkdir()
            literal_backslash.write_text("line\n" * 501, encoding="utf-8")
            slash_path.write_text("line\n" * 2, encoding="utf-8")
            self._git(root, "add", ".")
            staged = self._run(root, "--staged")
            self.assertEqual(1, staged.returncode, staged.stdout + staged.stderr)
            self._git(root, "commit", "-m", "backslash and slash sources")
            changed = self._run(root, "--changed", "--base", base)
            audited = self._run(root, "--all")
            self.assertEqual(1, changed.returncode, changed.stdout + changed.stderr)
            self.assertEqual(1, audited.returncode, audited.stdout + audited.stderr)
            self.assertIn("hard limit 500", changed.stdout)
            self.assertIn("hard limit 500", audited.stdout)

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
            (root / ".csharp-size-policy.json").write_text(json.dumps({
                "warningLines": 300, "errorLines": 500,
                "generatedGlobs": [], "legacyExemptions": []
            }), encoding="utf-8")
            result = self._run(root, "--staged")
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn("over.cs", result.stdout)

            source.write_text("line\n" * 301, encoding="utf-8")
            self._git(root, "add", ".")
            result = self._run(root, "--staged")
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("WARNING", result.stdout)

    def test_staged_mode_uses_staged_policy_when_working_tree_policy_diverges(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            policy = root / ".csharp-size-policy.json"
            policy.write_text(json.dumps({"warningLines": 300, "errorLines": 500,
                                          "generatedGlobs": [], "legacyExemptions": []}), encoding="utf-8")
            source = root / "staged.cs"
            source.write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", ".")
            policy.write_text(json.dumps({"warningLines": 300, "errorLines": 5000,
                                          "generatedGlobs": [], "legacyExemptions": []}), encoding="utf-8")
            result = self._run(root, "--staged")
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn("staged.cs", result.stdout)

    def test_all_mode_reads_committed_tree_when_index_has_add_delete_and_rename(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            (root / ".csharp-size-policy.json").write_text(
                '{"warningLines": 300, "errorLines": 500, "generatedGlobs": [], "legacyExemptions": []}',
                encoding="utf-8")
            original = root / "original.cs"
            original.write_text("line\n" * 501, encoding="utf-8")
            rename_source = root / "rename-source.cs"
            rename_source.write_text("line\n" * 2, encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "baseline")
            self._git(root, "rm", "original.cs")
            self._git(root, "mv", "rename-source.cs", "rename-destination.cs")
            (root / "new.cs").write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", "new.cs")
            result = self._run(root, "--all", "--report-only")
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("original.cs", result.stdout)
            self.assertNotIn("new.cs", result.stdout)
            self.assertNotIn("rename-destination.cs", result.stdout)
            self.assertIn("2 source file(s) checked", result.stdout)

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
            self._git(root, "add", ".")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            self._git(root, "commit", "-m", "baseline")
            result = self._run(root, "--changed", "--base", "missing-base")
            self.assertEqual(2, result.returncode)
            self.assertIn("Fetch the PR base", result.stderr)

    def test_workflow_uses_pr_base_or_origin_main_without_local_main_assumption(self):
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("check_csharp_size.py --changed --base", workflow)
        self.assertIn("refs/remotes/origin/main", workflow)
        self.assertIn("pull_request.base.sha", workflow)

    def test_installer_is_idempotent_and_preserves_unknown_backup_refusal(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            shutil.copy(ROOT / ".githooks/pre-commit", root / ".githooks/pre-commit")
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            target.write_text("#!/bin/sh\nexit 9\n", encoding="utf-8")
            first = self._run_installer(pwsh, root)
            self.assertEqual(0, first.returncode, first.stdout + first.stderr)
            backup = hooks / "pre-commit.savetoolkit-original"
            self.assertEqual("#!/bin/sh\nexit 9\n", backup.read_text(encoding="utf-8"))
            second = self._run_installer(pwsh, root)
            self.assertEqual(0, second.returncode, second.stdout + second.stderr)
            self.assertEqual("#!/bin/sh\nexit 9\n", backup.read_text(encoding="utf-8"))
            what_if = self._run_installer(pwsh, root, "-WhatIf")
            self.assertEqual(0, what_if.returncode, what_if.stdout + what_if.stderr)
            self.assertIn("What if", what_if.stdout + what_if.stderr)
            target.write_text("#!/bin/sh\nexit 8\n", encoding="utf-8")
            refused = self._run_installer(pwsh, root)
            self.assertNotEqual(0, refused.returncode)
            self.assertIn("existing chained hook", refused.stderr + refused.stdout)

    def test_installer_copy_failure_keeps_active_hook_and_does_not_create_backup(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            shutil.copy(ROOT / ".githooks/pre-commit", root / ".githooks/pre-commit")
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            original_bytes = b"#!/bin/sh\nexit 23\n"
            target.write_bytes(original_bytes)
            failed = self._run_installer_with_copy_failure(pwsh, root)
            self.assertNotEqual(0, failed.returncode)
            self.assertEqual(original_bytes, target.read_bytes())
            self.assertFalse((hooks / "pre-commit.savetoolkit-original").exists())
            self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))

    def test_hook_forwards_arguments_and_original_exit_code(self):
        shell = shutil.which("sh")
        if not shell:
            self.skipTest("POSIX sh is required to exercise the hook chain")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "scripts").mkdir()
            shutil.copy(ROOT / "scripts/check_csharp_size.py", root / "scripts/check_csharp_size.py")
            shutil.copy(ROOT / ".csharp-size-policy.json", root / ".csharp-size-policy.json")
            self._git(root, "init")
            hooks = root / ".git/hooks"
            hook = hooks / "pre-commit"
            shutil.copy(ROOT / ".githooks/pre-commit", hook)
            self._git(root, "add", ".")
            original = hooks / "pre-commit.savetoolkit-original"
            original.write_text("#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$HOOK_ARGS_PATH\"\nexit 17\n", encoding="utf-8")
            original.chmod(0o755)
            arguments_path = root / "hook arguments.txt"
            result = subprocess.run([shell, str(hook), "first", "two words"], cwd=root,
                                    env={**os.environ, "HOOK_ARGS_PATH": str(arguments_path)},
                                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            self.assertEqual(17, result.returncode, result.stdout + result.stderr)
            self.assertEqual(["first", "two words"], arguments_path.read_text().splitlines())
            arguments_path.unlink()
            original.chmod(0o644)
            disabled = subprocess.run([shell, str(hook), "disabled"], cwd=root,
                                      env={**os.environ, "HOOK_ARGS_PATH": str(arguments_path)},
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            self.assertEqual(0, disabled.returncode, disabled.stdout + disabled.stderr)
            self.assertFalse(arguments_path.exists())

    @staticmethod
    def _git(root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", "-C", str(root), *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def _run(self, root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run([sys.executable, str(SCRIPT), "--root", str(root), *args],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def _run_installer(self, pwsh: str, root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run([pwsh, "-NoProfile", "-File", str(root / "scripts/install-hooks.ps1"), *args],
                              cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def _run_installer_with_copy_failure(self, pwsh: str, root: Path) -> subprocess.CompletedProcess:
        script = str(root / "scripts/install-hooks.ps1").replace("'", "''")
        command = "function Copy-Item { throw 'injected copy failure' }; & '" + script + "'"
        return subprocess.run([pwsh, "-NoProfile", "-Command", command], cwd=root,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


if __name__ == "__main__":
    unittest.main()
