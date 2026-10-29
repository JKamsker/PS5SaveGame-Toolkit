"""Regressions for CI summaries; no downloaded packages or test runners required."""

import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET


SCRIPT = Path(__file__).resolve().parents[1] / "scripts/ci_summary.py"


class SummaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("ci_summary", SCRIPT)
        cls.summary = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.summary)

    def test_trx_counts_include_skips_and_unusual_failed_outcomes(self):
        report = ET.fromstring('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary><Counters total="10" passed="5" failed="1" error="1" timeout="1"
            aborted="1" notExecuted="1" /></ResultSummary></TestRun>''')
        self.assertEqual((5, 4, 1), self.summary.trx_counts(report))

    def test_junit_counts_leaf_suites_without_counting_aggregate_twice(self):
        report = ET.fromstring('''<testsuites tests="9" failures="1" errors="1" skipped="2">
          <testsuite tests="9" failures="1" errors="1" skipped="2">
            <testsuite tests="6" failures="1" errors="0" skipped="1" />
            <testsuite tests="3" failures="0" errors="1" skipped="1" />
          </testsuite></testsuites>''')
        self.assertEqual((5, 2, 2), self.summary.junit_counts(report))

    def test_empty_junit_is_explicitly_zero_tests(self):
        self.assertEqual((0, 0, 0), self.summary.junit_counts(ET.fromstring('<testsuites />')))

    def test_browser_summary_combines_both_nested_suite_reports(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for suite, tests, skipped in (("canonical", 66, 1), ("unreal", 10, 0)):
                report = root / "invocation-test" / suite / "e2e.xml"
                report.parent.mkdir(parents=True)
                report.write_text(f'<testsuite tests="{tests}" failures="0" skipped="{skipped}" />', encoding="utf-8")
            summary = root / "summary.md"
            with patch("sys.argv", ["ci_summary", "--title", "Browser", "--junit", str(root)]), \
                 patch.dict("os.environ", {"GITHUB_STEP_SUMMARY": str(summary)}):
                self.summary.main()
            self.assertIn("| JUnit: 2 report(s) | 75 | 0 | 1 |", summary.read_text(encoding="utf-8"))

    def test_missing_trx_counters_are_not_reported_as_zero_passed(self):
        with self.assertRaises(ValueError):
            self.summary.trx_counts(ET.fromstring('<TestRun />'))

    def test_script_summary_uses_final_counts_without_grepping_pass_lines(self):
        log = 'PASS first\nScript tests: 12 passed, 1 failed, 0 skipped.\n'
        self.assertEqual((12, 1, 0), self.summary.script_counts(log))
        self.assertIsNone(self.summary.script_counts('PASS first\ninterrupted'))

    def test_partial_reports_still_count_completed_files_and_explain_missing_data(self):
        with tempfile.TemporaryDirectory() as directory:
            valid = Path(directory) / "web.xml"
            valid.write_text('<testsuite tests="5" failures="1" skipped="1" />', encoding="utf-8")
            missing = Path(directory) / "missing.xml"
            output = "".join(self.summary.test_rows([valid, missing], self.summary.junit_counts, "Web"))
            self.assertIn("| Web: 1 report(s) | 3 | 1 | 1 |", output)
            self.assertIn("Report unavailable: `missing.xml`", output)
            self.assertIn("No completed test reports", "".join(self.summary.test_rows([], self.summary.trx_counts)))

    def test_bundle_summary_lists_the_real_archive_name_size_and_checksum(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "SaveToolkit-1.2.3-win-x64.zip").write_bytes(b"bundle")
            checksum = "a" * 64
            (root / "SaveToolkit-1.2.3-win-x64.zip.sha256").write_text(
                f"{checksum}  SaveToolkit-1.2.3-win-x64.zip\n", encoding="utf-8")
            output = "".join(self.summary.bundle_rows(root))
            self.assertIn("SaveToolkit-1.2.3-win-x64.zip", output)
            self.assertIn(f"| 6 | `{checksum}` |", output)


if __name__ == "__main__":
    unittest.main()
