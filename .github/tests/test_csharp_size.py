"""Boundary and workflow regressions for the C# source-size guard."""

import importlib.util
import json
import os
from pathlib import Path
import shutil
import socket
import stat
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

    def test_legacy_exemption_must_exceed_baseline_and_selected_error_limits(self):
        for max_lines in (450, 500):
            with self.subTest(max_lines=max_lines):
                with self.assertRaises(self.guard.GuardError):
                    self.guard.load_policy_bytes(json.dumps({
                        "warningLines": 300, "errorLines": 400, "generatedGlobs": [],
                        "legacyExemptions": [{"path": "legacy.cs", "maxLines": max_lines,
                                              "reason": "reviewed"}]
                    }).encode(), "test policy")
        policy = self.guard.load_policy_bytes(json.dumps({
            "warningLines": 300, "errorLines": 400, "generatedGlobs": [],
            "legacyExemptions": [{"path": "legacy.cs", "maxLines": 501,
                                  "reason": "reviewed"}]
        }).encode(), "test policy")
        self.assertIn("legacy.cs", policy.legacy_exemptions)

    def test_type_change_from_symlink_to_regular_file_is_checked(self):
        if os.name == "nt":
            self.skipTest("POSIX symlink type changes are required for this regression")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            (root / ".csharp-size-policy.json").write_text(json.dumps({
                "warningLines": 300, "errorLines": 500,
                "generatedGlobs": [], "legacyExemptions": []
            }), encoding="utf-8")
            (root / "small.cs").write_text("line\n" * 2, encoding="utf-8")
            (root / "Foo.cs").symlink_to("small.cs")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "symlink baseline")
            base = self._git(root, "rev-parse", "HEAD").stdout.strip()
            (root / "Foo.cs").unlink()
            (root / "Foo.cs").write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", "-A")
            status = self._git(root, "diff", "--cached", "--name-status").stdout
            self.assertIn("\tFoo.cs", status)
            staged = self._run(root, "--staged")
            self.assertEqual(1, staged.returncode, staged.stdout + staged.stderr)
            self._git(root, "commit", "-m", "regular type change")
            changed = self._run(root, "--changed", "--base", base)
            self.assertEqual(1, changed.returncode, changed.stdout + changed.stderr)

    def test_literal_backslash_legacy_exemption_matches_all_modes(self):
        if os.name == "nt":
            self.skipTest("POSIX permits literal backslash filenames used by this policy regression")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._git(root, "init")
            self._git(root, "config", "user.email", "guard@example.invalid")
            self._git(root, "config", "user.name", "Guard Test")
            policy = {
                "warningLines": 300, "errorLines": 500, "generatedGlobs": [],
                "legacyExemptions": [
                    {"path": "a\\b.cs", "maxLines": 600, "reason": "literal POSIX path fixture"},
                    {"path": "\\leading.cs", "maxLines": 600, "reason": "leading backslash fixture"},
                    {"path": "a\\\\b.cs", "maxLines": 600, "reason": "repeated backslash fixture"},
                    {"path": "a\\..\\legacy.cs", "maxLines": 600, "reason": "literal dot component fixture"},
                ]
            }
            (root / ".csharp-size-policy.json").write_text(json.dumps(policy), encoding="utf-8")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "policy baseline")
            base = self._git(root, "rev-parse", "HEAD").stdout.strip()
            sources = [root / "a\\b.cs", root / "\\leading.cs",
                       root / "a\\\\b.cs", root / "a\\..\\legacy.cs"]
            for source in sources:
                source.write_text("line\n" * 501, encoding="utf-8")
            (root / "a" / "b.cs").parent.mkdir()
            (root / "a" / "b.cs").write_text("line\n" * 2, encoding="utf-8")
            self._git(root, "add", ".")
            staged = self._run(root, "--staged")
            self.assertEqual(0, staged.returncode, staged.stdout + staged.stderr)
            self.assertIn("literal POSIX path fixture", staged.stdout)
            self.assertIn("leading backslash fixture", staged.stdout)
            self.assertIn("repeated backslash fixture", staged.stdout)
            self.assertIn("literal dot component fixture", staged.stdout)
            self._git(root, "commit", "-m", "literal path")
            changed = self._run(root, "--changed", "--base", base)
            audited = self._run(root, "--all")
            self.assertEqual(0, changed.returncode, changed.stdout + changed.stderr)
            self.assertEqual(0, audited.returncode, audited.stdout + audited.stderr)
            self.assertIn("a\\b.cs", changed.stdout)
            self.assertIn("a\\b.cs", audited.stdout)
            self.assertIn("\\leading.cs", changed.stdout)
            self.assertIn("a\\\\b.cs", changed.stdout)
            self.assertIn("a\\..\\legacy.cs", changed.stdout)

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
            source = root / ".githooks/pre-commit"
            self._copy_committed_hook(source)
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            target.write_text("#!/bin/sh\nexit 9\n", encoding="utf-8")
            first = self._run_installer(pwsh, root)
            self.assertEqual(0, first.returncode, first.stdout + first.stderr)
            backup = hooks / "pre-commit.savetoolkit-original"
            self.assertEqual("#!/bin/sh\nexit 9\n", backup.read_text(encoding="utf-8"))
            second = self._run_installer(pwsh, root)
            self.assertEqual(0, second.returncode, second.stdout + second.stderr)
            self.assertEqual(source.read_bytes(), target.read_bytes())
            self.assertIn("already installed", second.stdout + second.stderr)
            self.assertEqual("#!/bin/sh\nexit 9\n", backup.read_text(encoding="utf-8"))
            if os.name != "nt":
                self.assertNotEqual(0, stat.S_IMODE(target.stat().st_mode) & 0o111)
                target_before_mode_only_dirty = target.read_bytes()
                target_before_mode_only_dirty_mode = stat.S_IMODE(target.stat().st_mode)
                source.chmod(0o644)
                for arguments in ((), ("-WhatIf",)):
                    with self.subTest(mode_only_dirty=True, arguments=arguments):
                        mode_only_dirty = self._run_installer_bounded(pwsh, root, *arguments)
                        self.assertNotEqual(0, mode_only_dirty.returncode)
                        self.assertIn("must remain executable", mode_only_dirty.stderr + mode_only_dirty.stdout)
                        self.assertEqual(target_before_mode_only_dirty, target.read_bytes())
                        self.assertEqual(target_before_mode_only_dirty_mode,
                                         stat.S_IMODE(target.stat().st_mode))
                source.chmod(0o755)
                target.chmod(0o644)
                before_mode_reset = target.read_bytes()
                mode_reset = self._run_installer(pwsh, root)
                self.assertEqual(0, mode_reset.returncode, mode_reset.stdout + mode_reset.stderr)
                self.assertEqual(before_mode_reset, target.read_bytes())
                self.assertEqual(stat.S_IMODE(source.stat().st_mode), stat.S_IMODE(target.stat().st_mode))
                self.assertNotEqual(0, stat.S_IMODE(target.stat().st_mode) & 0o111)
            what_if = self._run_installer(pwsh, root, "-WhatIf")
            self.assertEqual(0, what_if.returncode, what_if.stdout + what_if.stderr)
            self.assertIn("What if", what_if.stdout + what_if.stderr)
            target.write_text("#!/bin/sh\nexit 8\n", encoding="utf-8")
            refused = self._run_installer(pwsh, root)
            self.assertNotEqual(0, refused.returncode)
            self.assertIn("existing chained hook", refused.stderr + refused.stdout)

    def test_installer_rejects_dirty_source_even_when_target_matches_dirty_bytes(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            source = root / ".githooks/pre-commit"
            self._copy_committed_hook(source)
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            dirty = source.read_bytes() + b"# local uncommitted change\n"
            source.write_bytes(dirty)
            target.write_bytes(dirty)
            target.chmod(0o644)
            before_mode = stat.S_IMODE(target.stat().st_mode)
            for arguments in ((), ("-WhatIf",)):
                with self.subTest(arguments=arguments):
                    result = self._run_installer_bounded(pwsh, root, *arguments)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("differs from the committed wrapper", result.stderr + result.stdout)
                    self.assertEqual(dirty, target.read_bytes())
                    self.assertEqual(before_mode, stat.S_IMODE(target.stat().st_mode))
                    self.assertFalse((hooks / "pre-commit.savetoolkit-original").exists())
                    self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))

    def test_installer_rejects_backup_only_collisions_before_preparation(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            self._copy_committed_hook(root / ".githooks/pre-commit")
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            backup = hooks / "pre-commit.savetoolkit-original"
            for mode in (0o755, 0o644):
                backup_bytes = f"#!/bin/sh\nexit {mode}\n".encode()
                backup.write_bytes(backup_bytes)
                backup.chmod(mode)
                before_mode = stat.S_IMODE(backup.stat().st_mode)
                for arguments in ((), ("-WhatIf",)):
                    with self.subTest(mode=oct(mode), arguments=arguments):
                        result = self._run_installer_bounded(pwsh, root, *arguments)
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("cannot be tied", result.stderr + result.stdout)
                        self.assertFalse(target.exists())
                        self.assertEqual(backup_bytes, backup.read_bytes())
                        self.assertEqual(before_mode, stat.S_IMODE(backup.stat().st_mode))
                        self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))
                backup.unlink()

    def test_installer_rejects_disabled_source_before_fresh_install(self):
        if os.name == "nt":
            self.skipTest("POSIX executable mode is required for this regression")
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            source = root / ".githooks/pre-commit"
            self._copy_committed_hook(source)
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            source.chmod(0o644)
            hooks = root / ".git/hooks"
            for arguments in ((), ("-WhatIf",)):
                with self.subTest(arguments=arguments):
                    refused = self._run_installer_bounded(pwsh, root, *arguments)
                    self.assertNotEqual(0, refused.returncode)
                    self.assertIn("must remain executable", refused.stderr + refused.stdout)
                    self.assertFalse((hooks / "pre-commit").exists())
                    self.assertFalse((hooks / "pre-commit.savetoolkit-original").exists())
                    self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))

    def test_git_dispatches_executable_installed_hook(self):
        if os.name == "nt":
            self.skipTest("POSIX Git hook activation is required for this regression")
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            self._copy_committed_hook(root / ".githooks/pre-commit")
            shutil.copy(ROOT / "scripts/check_csharp_size.py", root / "scripts/check_csharp_size.py")
            shutil.copy(ROOT / ".csharp-size-policy.json", root / ".csharp-size-policy.json")
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            installed = self._run_installer(pwsh, root)
            self.assertEqual(0, installed.returncode, installed.stdout + installed.stderr)
            target = root / ".git/hooks/pre-commit"
            self.assertNotEqual(0, stat.S_IMODE(target.stat().st_mode) & 0o111)
            oversized = root / "TooLarge.cs"
            oversized.write_text("line\n" * 501, encoding="utf-8")
            self._git(root, "add", "TooLarge.cs")
            environment = {
                **os.environ,
                "GIT_AUTHOR_NAME": "C# size guard tests",
                "GIT_AUTHOR_EMAIL": "csharp-size-guard-tests@example.invalid",
                "GIT_COMMITTER_NAME": "C# size guard tests",
                "GIT_COMMITTER_EMAIL": "csharp-size-guard-tests@example.invalid",
            }
            blocked = subprocess.run(["git", "-C", str(root), "commit", "-m", "blocked"],
                                     env=environment, stdout=subprocess.PIPE,
                                     stderr=subprocess.PIPE, text=True)
            self.assertNotEqual(0, blocked.returncode, blocked.stdout + blocked.stderr)
            self.assertIn("hard limit", blocked.stdout + blocked.stderr)

    def test_installer_copy_failure_keeps_active_hook_and_does_not_create_backup(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            self._copy_committed_hook(root / ".githooks/pre-commit")
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            original_bytes = b"#!/bin/sh\nexit 23\n"
            target.write_bytes(original_bytes)
            failed = self._run_installer_with_copy_failure(pwsh, root)
            self.assertNotEqual(0, failed.returncode)
            self.assertEqual(original_bytes, target.read_bytes())
            self.assertFalse((hooks / "pre-commit.savetoolkit-original").exists())
            self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))

    def test_installer_preserves_unrelated_marker_hook_and_rejects_entry_collisions(self):
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the installer")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            source = root / ".githooks/pre-commit"
            self._copy_committed_hook(source)
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            unrelated = (b"#!/bin/sh\n# Optional repository hook installed by scripts/install-hooks.ps1.\n"
                         b"# check_csharp_size.py is mentioned here, but this is unrelated.\nexit 19\n")
            target.write_bytes(unrelated)
            installed = self._run_installer(pwsh, root)
            self.assertEqual(0, installed.returncode, installed.stdout + installed.stderr)
            self.assertEqual(unrelated, (hooks / "pre-commit.savetoolkit-original").read_bytes())
            self.assertEqual(source.read_bytes(), target.read_bytes())

            target.unlink()
            target.mkdir()
            (target / "sentinel").write_text("target", encoding="utf-8")
            collision = self._run_installer(pwsh, root)
            self.assertNotEqual(0, collision.returncode)
            self.assertTrue(target.is_dir())
            self.assertEqual("target", (target / "sentinel").read_text(encoding="utf-8"))

            (target / "sentinel").unlink()
            target.rmdir()
            target.write_bytes(unrelated)
            backup = hooks / "pre-commit.savetoolkit-original"
            backup.unlink()
            backup.mkdir()
            (backup / "sentinel").write_text("backup", encoding="utf-8")
            collision = self._run_installer(pwsh, root)
            self.assertNotEqual(0, collision.returncode)
            self.assertEqual(unrelated, target.read_bytes())
            self.assertTrue(backup.is_dir())
            self.assertEqual("backup", (backup / "sentinel").read_text(encoding="utf-8"))

    def test_installer_rejects_posix_special_entries_without_read_or_mutation(self):
        if os.name == "nt":
            self.skipTest("POSIX special filesystem entries are required for this regression")
        pwsh = shutil.which("pwsh")
        if not pwsh:
            self.skipTest("PowerShell 7 is required to exercise the POSIX installer fixture")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".githooks").mkdir()
            (root / "scripts").mkdir()
            self._copy_committed_hook(root / ".githooks/pre-commit")
            shutil.copy(ROOT / "scripts/install-hooks.ps1", root / "scripts/install-hooks.ps1")
            self._git(root, "init")
            self._git(root, "add", ".")
            self._git(root, "commit", "-m", "committed hook")
            hooks = root / ".git/hooks"
            target = hooks / "pre-commit"
            backup = hooks / "pre-commit.savetoolkit-original"
            os.mkfifo(target)
            for arguments in ((), ("-WhatIf",)):
                with self.subTest(entry="FIFO target", arguments=arguments):
                    result = self._run_installer_bounded(pwsh, root, *arguments)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("Unsupported filesystem entry", result.stderr + result.stdout)
                    self.assertTrue(stat.S_ISFIFO(target.stat().st_mode))
                    self.assertFalse(backup.exists())
                    self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))
            target.unlink()
            server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            try:
                server.bind(str(backup))
                for arguments in ((), ("-WhatIf",)):
                    with self.subTest(entry="Unix socket backup", arguments=arguments):
                        result = self._run_installer_bounded(pwsh, root, *arguments)
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("Unsupported filesystem entry", result.stderr + result.stdout)
                        self.assertFalse(target.exists())
                        self.assertTrue(backup.exists())
                        self.assertEqual([], list(hooks.glob(".pre-commit.savetoolkit-*.tmp")))
            finally:
                server.close()
                backup.unlink()
            source = root / ".githooks/pre-commit"
            source.unlink()
            os.mkfifo(source)
            for arguments in ((), ("-WhatIf",)):
                with self.subTest(entry="FIFO source", arguments=arguments):
                    result = self._run_installer_bounded(pwsh, root, *arguments)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("Missing or unsupported checked-in hook", result.stderr + result.stdout)
                    self.assertFalse(target.exists())
                    self.assertFalse(backup.exists())

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
    def _copy_committed_hook(destination: Path) -> None:
        command = ["git", "-C", str(ROOT), "cat-file", "blob", "HEAD:.githooks/pre-commit"]
        result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if result.returncode != 0 and os.name != "nt":
            git_file = ROOT / ".git"
            if git_file.is_file():
                marker = git_file.read_text(encoding="utf-8").strip()
                if marker.startswith("gitdir: "):
                    raw_git_dir = marker.removeprefix("gitdir: ").strip()
                    if len(raw_git_dir) >= 3 and raw_git_dir[1] == ":":
                        git_dir = Path("/mnt") / raw_git_dir[0].lower() / raw_git_dir[3:]
                        result = subprocess.run(
                            ["git", "cat-file", "blob", "HEAD:.githooks/pre-commit"],
                            cwd=ROOT,
                            env={**os.environ, "GIT_DIR": str(git_dir), "GIT_WORK_TREE": str(ROOT)},
                            stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE,
                        )
        if result.returncode != 0:
            raise subprocess.CalledProcessError(result.returncode, command,
                                                output=result.stdout, stderr=result.stderr)
        destination.write_bytes(result.stdout)
        if os.name != "nt":
            destination.chmod(0o755)

    @staticmethod
    def _git(root: Path, *args: str) -> subprocess.CompletedProcess:
        environment = None
        if args and args[0] == "commit":
            environment = {
                **os.environ,
                "GIT_AUTHOR_NAME": "C# size guard tests",
                "GIT_AUTHOR_EMAIL": "csharp-size-guard-tests@example.invalid",
                "GIT_COMMITTER_NAME": "C# size guard tests",
                "GIT_COMMITTER_EMAIL": "csharp-size-guard-tests@example.invalid",
            }
        return subprocess.run(["git", "-C", str(root), *args], check=True,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                              env=environment)

    def _run(self, root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run([sys.executable, str(SCRIPT), "--root", str(root), *args],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def _run_installer(self, pwsh: str, root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run([pwsh, "-NoProfile", "-File", str(root / "scripts/install-hooks.ps1"), *args],
                              cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def _run_installer_bounded(self, pwsh: str, root: Path, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run([pwsh, "-NoProfile", "-File", str(root / "scripts/install-hooks.ps1"), *args],
                              cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                              timeout=15)

    def _run_installer_with_copy_failure(self, pwsh: str, root: Path) -> subprocess.CompletedProcess:
        script = str(root / "scripts/install-hooks.ps1").replace("'", "''")
        command = "function Copy-Item { throw 'injected copy failure' }; & '" + script + "'"
        return subprocess.run([pwsh, "-NoProfile", "-Command", command], cwd=root,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


if __name__ == "__main__":
    unittest.main()
