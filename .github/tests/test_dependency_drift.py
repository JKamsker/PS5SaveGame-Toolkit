"""Dependency reporting must distinguish updates from failed or incomplete queries."""

import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


class DriftTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        script = Path(__file__).resolve().parents[1] / "scripts/dependency_drift.py"
        spec = importlib.util.spec_from_file_location("dependency_drift", script)
        cls.drift = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.drift)

    def query(self, code, text, accepted=(0, 1)):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "outdated.json"
            result = subprocess.CompletedProcess([], code, text, "diagnostic")
            with patch.object(self.drift.subprocess, "run", return_value=result):
                data = self.drift.query(["npm", "outdated"], Path(directory), output, accepted)
            self.assertEqual(text, output.read_text(encoding="utf-8"))
            self.assertEqual("diagnostic", output.with_suffix(".stderr.log").read_text(encoding="utf-8"))
            return data

    def test_outdated_exit_one_with_valid_updates_is_a_report(self):
        self.assertEqual({"react": {"latest": "20.0.0"}}, self.query(1, '{"react":{"latest":"20.0.0"}}'))

    def test_registry_error_exit_one_is_not_mistaken_for_an_update(self):
        self.assertIsNone(self.query(1, '{"error":{"code":"EAI_AGAIN"}}'))

    def test_non_json_output_and_other_exit_codes_are_unavailable(self):
        self.assertIsNone(self.query(1, "registry unavailable"))
        self.assertIsNone(self.query(2, "{}"))

    def test_nuget_errors_are_not_reported_as_no_updates(self):
        self.assertIsNone(self.query(0, '{"version":1,"problems":[{"severity":"error"}]}', (0,)))

    def test_zero_updates_remains_a_successful_report(self):
        self.assertEqual({}, self.query(0, "{}"))

    def test_timeout_is_retained_as_unavailable_diagnostic(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "npm.json"
            with patch.object(self.drift.subprocess, "run", side_effect=subprocess.TimeoutExpired("npm", 180)):
                self.assertIsNone(self.drift.query(["npm"], root, output, (0, 1)))
            self.assertIn("TimeoutExpired", output.with_suffix(".stderr.log").read_text(encoding="utf-8"))

    def test_report_survives_restore_failure_and_still_queries_npm(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "web").mkdir()
            (root / "src").mkdir()
            (root / "src/Demo.csproj").write_text('<Project />', encoding="utf-8")
            output = root / "reports"
            summary = root / "job-summary.md"
            replies = [subprocess.CompletedProcess([], 1, "", "restore unavailable"),
                       subprocess.CompletedProcess([], 0, "installed", ""),
                       subprocess.CompletedProcess([], 1, '{"react":{"latest":"20.0.0"}}', "")]
            with (patch.object(self.drift, "ROOT", root),
                  patch.object(self.drift.subprocess, "run", side_effect=replies) as run,
                  patch.object(sys, "argv", ["dependency_drift.py", "--output-directory", str(output)]),
                  patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary)})):
                self.drift.main()
            text = summary.read_text(encoding="utf-8")
            self.assertIn("NuGet report unavailable", text)
            self.assertIn("npm: **1** direct dependency update(s)", text)
            self.assertEqual(3, run.call_count)
            self.assertEqual('<Project />', (root / "src/Demo.csproj").read_text(encoding="utf-8"))
            self.assertTrue((output / "dependencies.slnx").exists())

    def test_successful_report_deduplicates_nuget_updates_without_changing_projects(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "web").mkdir()
            (root / "src").mkdir()
            (root / "src/Demo.csproj").write_text('<Project />', encoding="utf-8")
            output = root / "reports"
            summary = root / "job-summary.md"
            packages = ('{"projects":[{"frameworks":[{"topLevelPackages":['
                        '{"id":"Example","resolvedVersion":"1.0.0","latestVersion":"1.1.0"},'
                        '{"id":"Example","resolvedVersion":"1.0.0","latestVersion":"1.1.0"}]}]}]}')
            replies = [subprocess.CompletedProcess([], 0, "restored", ""),
                       subprocess.CompletedProcess([], 0, packages, ""),
                       subprocess.CompletedProcess([], 0, "installed", ""),
                       subprocess.CompletedProcess([], 0, "{}", "")]
            with (patch.object(self.drift, "ROOT", root),
                  patch.object(self.drift.subprocess, "run", side_effect=replies) as run,
                  patch.object(sys, "argv", ["dependency_drift.py", "--output-directory", str(output)]),
                  patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary)})):
                self.drift.main()
            text = summary.read_text(encoding="utf-8")
            self.assertIn("NuGet: **1** distinct package/version update(s)", text)
            self.assertIn("npm: **0** direct dependency update(s)", text)
            self.assertEqual(4, run.call_count)
            self.assertEqual(1, text.count("`Example`"))
            self.assertEqual('<Project />', (root / "src/Demo.csproj").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
