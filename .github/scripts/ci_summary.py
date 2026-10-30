"""Append report-derived test counts or bundle inventory to a GitHub job summary."""

import argparse
import os
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def trx_counts(root):
    counters = root.find(".//{*}ResultSummary/{*}Counters")
    if counters is None:
        raise ValueError("TRX counters missing")
    passed = int(counters.get("passed", 0))
    skipped = int(counters.get("notExecuted", 0))
    # Count all non-passed/non-skipped outcomes, including runner errors and timeouts.
    return passed, int(counters.attrib["total"]) - passed - skipped, skipped


def junit_counts(root):
    suites = [suite for suite in root.iter("testsuite") if suite.find("testsuite") is None]
    totals = [0, 0, 0]
    for suite in suites:
        failed = int(suite.get("failures", 0)) + int(suite.get("errors", 0))
        skipped = int(suite.get("skipped", 0))
        values = int(suite.attrib["tests"]) - failed - skipped, failed, skipped
        totals = [left + right for left, right in zip(totals, values)]
    return tuple(totals)


def script_counts(text):
    match = re.search(r"Script tests: (\d+) passed, (\d+) failed, (\d+) skipped\.", text)
    return tuple(map(int, match.groups())) if match else None


def test_rows(paths, parser, label="Tests"):
    totals = [0, 0, 0]
    reports = 0
    for path in paths:
        try:
            counts = parser(ET.parse(path).getroot())
            if any(count < 0 for count in counts):
                raise ValueError("Invalid test counters")
            totals = [left + right for left, right in zip(totals, counts)]
            reports += 1
        except (OSError, ET.ParseError, KeyError, ValueError) as error:
            yield f"| Report unavailable: `{path.name}` ({type(error).__name__}) | — | — | — |\n"
    if reports:
        yield f"| {label}: {reports} report(s) | {totals[0]} | {totals[1]} | {totals[2]} |\n"
    else:
        yield f"| {label}: No completed test reports available; consult the job log | — | — | — |\n"


def bundle_rows(directory):
    files = sorted(directory.glob("*.sha256"))
    if not files:
        yield "| No completed bundles available; consult the job log | — | — |\n"
    for checksum in files:
        digest, name = checksum.read_text(encoding="utf-8").strip().split("  ", 1)
        bundle = directory / name
        yield f"| `{name}` | {bundle.stat().st_size:,} | `{digest}` |\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--title", required=True)
    parser.add_argument("--trx-directory", type=Path)
    parser.add_argument("--junit", type=Path)
    parser.add_argument("--script-log", type=Path)
    parser.add_argument("--packages", type=Path)
    args = parser.parse_args()
    lines = [f"### {args.title} — {os.environ.get('JOB_STATUS', 'unknown')}\n\n"]
    if args.trx_directory or args.junit:
        lines += ["| Reports | Passed | Failed / other | Skipped |\n", "| --- | ---: | ---: | ---: |\n"]
        if args.trx_directory:
            lines += test_rows(sorted(args.trx_directory.rglob("*.trx")), trx_counts, "Backend TRX")
        if args.junit:
            reports = sorted(args.junit.rglob("e2e.xml")) if args.junit.is_dir() else [args.junit]
            lines += test_rows(reports, junit_counts, "JUnit")
    if args.script_log:
        text = args.script_log.read_text(encoding="utf-8-sig") if args.script_log.exists() else ""
        counts = script_counts(text)
        lines += [f"Script regressions: {counts[0]} passed, {counts[1]} failed, {counts[2]} skipped.\n"
                  if counts else "Script regression counts unavailable; consult the job log.\n"]
    if args.packages:
        lines += ["| Bundle | Bytes | SHA-256 |\n", "| --- | ---: | --- |\n", *bundle_rows(args.packages)]
        lines += ["\nBundle creation and smoke-test status are recorded in the steps above.\n"]
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write("".join(lines) + "\n")


if __name__ == "__main__":
    main()
