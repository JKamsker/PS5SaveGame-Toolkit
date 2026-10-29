"""Report NuGet/npm version drift without changing dependencies or enforcing a gate."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]


def command(arguments, cwd, output):
    try:
        result = subprocess.run(arguments, cwd=cwd, capture_output=True, text=True, encoding="utf-8", timeout=180)
    except (OSError, subprocess.TimeoutExpired) as error:
        result = subprocess.CompletedProcess(arguments, 1, "", f"Command unavailable: {type(error).__name__}")
    output.write_text(result.stdout, encoding="utf-8")
    output.with_suffix(".stderr.log").write_text(result.stderr, encoding="utf-8")
    return result


def query(arguments, cwd, output, accepted=(0,)):
    result = command(arguments, cwd, output)
    if result.returncode not in accepted:
        return None
    try:
        data = json.loads(result.stdout)
    except ValueError:
        return None
    if not isinstance(data, dict) or "error" in data or data.get("problems"):
        return None
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-directory", type=Path, required=True)
    args = parser.parse_args()
    output = args.output_directory.resolve()
    output.mkdir(parents=True, exist_ok=True)
    solution = ET.Element("Solution")
    for folder in ("src", "tests", "plugins"):
        for project in sorted((ROOT / folder).rglob("*.csproj")):
            if not {"bin", "obj"}.intersection(project.relative_to(ROOT).parts):
                ET.SubElement(solution, "Project", Path=str(project))
    solution_path = output / "dependencies.slnx"
    ET.ElementTree(solution).write(solution_path, encoding="utf-8", xml_declaration=True)
    notes = ["### Weekly dependency drift (report-only)\n\n",
             "No dependency updates, auto-merge or publication. Stable NuGet and direct npm packages only; "
             "GitHub Actions upgrades are covered by Dependabot. Raw reports and diagnostics are in the artifact.\n\n"]
    dotnet = shutil.which("dotnet") or "dotnet"
    restore = command([dotnet, "restore", str(solution_path)], ROOT, output / "nuget-restore.log")
    nuget = None
    if restore.returncode == 0:
        nuget = query([dotnet, "package", "list", "--project", str(solution_path), "--no-restore",
                       "--outdated", "--format", "json", "--output-version", "1"], ROOT, output / "nuget.json")
    if nuget is None or not nuget.get("projects"):
        notes.append("NuGet report unavailable or incomplete; inspect restore/query logs.\n\n")
    else:
        packages = {(package["id"], package["resolvedVersion"], package["latestVersion"])
                    for project in nuget["projects"] for framework in project.get("frameworks", [])
                    for package in framework.get("topLevelPackages", [])}
        notes.append(f"NuGet: **{len(packages)}** distinct package/version update(s) across all projects.\n\n")
        notes += [f"- `{name}`: {current} → {latest}\n" for name, current, latest in sorted(packages)]
    npm = shutil.which("npm") or "npm"
    install = command([npm, "ci", "--no-audit", "--no-fund", "--ignore-scripts"], ROOT / "web", output / "npm-ci.log")
    npm_data = None
    if install.returncode == 0:
        # npm outdated returns 1 for available upgrades as well as some query errors.
        npm_data = query([npm, "outdated", "--json"], ROOT / "web", output / "npm.json", (0, 1))
    if npm_data is None:
        notes.append("\nnpm report unavailable or incomplete; inspect install/query logs.\n")
    else:
        notes.append(f"\nnpm: **{len(npm_data)}** direct dependency update(s); see npm.json for current/wanted/latest.\n")
    report = "".join(notes)
    (output / "summary.md").write_text(report, encoding="utf-8")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(report)


if __name__ == "__main__":
    main()
