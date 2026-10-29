"""Read-only documentation checks; run with Python 3.10+ from any directory.

Use --code-ref main to compare working-tree documentation with local main.
No samples, network, third-party dependencies or generated files are needed.
"""

import argparse
from functools import lru_cache
import json
from pathlib import Path
import re
import subprocess
import unittest
from urllib.parse import unquote, urlsplit


ROOT = Path(__file__).resolve().parents[2]
CODE_REF = "HEAD"
EXCLUDED = ROOT / "docs/LICENCE_EXCLUSIONS.md"


def document(path):
    return (ROOT / path).read_text(encoding="utf-8")


@lru_cache(maxsize=None)
def source(path):
    return subprocess.check_output(
        ["git", "show", f"{CODE_REF}:{path}"], cwd=ROOT, encoding="utf-8"
    )


@lru_cache(maxsize=None)
def source_paths():
    return set(subprocess.check_output(
        ["git", "ls-tree", "-r", "--name-only", CODE_REF], cwd=ROOT, encoding="utf-8"
    ).splitlines())


def plugins():
    for path in sorted(source_paths()):
        match = re.fullmatch(r"plugins/([^/]+)/SaveToolkit\.Plugins\.\1/\1Plugin\.cs", path)
        if match:
            code = source(path)
            plugin_id = re.search(r'PluginInfo Info.*?new\("([^"]+)"', code)[1]
            maturity = re.search(r"Maturity = PluginMaturity\.(\w+)", code)[1]
            yield match[1], plugin_id, maturity


def heading_anchors(text):
    anchors = set()
    counts = {}
    for title in re.findall(r"^#{1,6}\s+(.+?)\s*#*\s*$", text, re.MULTILINE):
        slug = re.sub(r"[^\w\- ]", "", title.lower()).replace(" ", "-")
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        anchors.add(f"{slug}-{count}" if count else slug)
    return anchors


class DocumentationTests(unittest.TestCase):
    def test_support_tables_match_registered_plugins(self):
        for page in ("README.md", "docs/PLUGINS.md"):
            text = document(page)
            for name, plugin_id, maturity in plugins():
                with self.subTest(page=page, plugin=name):
                    rows = [line for line in text.splitlines()
                            if line.startswith("| ") and f"plugins/{name}/README.md)" in line]
                    self.assertEqual(1, len(rows), "Each plugin needs exactly one support row")
                    self.assertEqual(maturity, rows[0].split("|")[2 if page == "README.md" else 3].strip())
                    if page == "docs/PLUGINS.md":
                        self.assertIn(f"`{plugin_id}`", rows[0])
                        code = source(f"plugins/{name}/SaveToolkit.Plugins.{name}/{name}Plugin.cs")
                        titles = set(re.findall(r'"(PPSA\d{5})"', code.split("Maturity =", 1)[0]))
                        self.assertEqual(titles, set(re.findall(r"PPSA\d{5}", rows[0])))
            count = re.search(r"\*\*(\d+) game/format plugins\*\*", document("README.md"))
            self.assertIsNotNone(count)
            self.assertEqual(len(list(plugins())), int(count[1]))

    def test_local_markdown_links_resolve(self):
        pages = [ROOT / "README.md", ROOT / "AGENTS.md", *sorted((ROOT / "docs").rglob("*.md"))]
        for page in pages:
            if page == EXCLUDED:
                continue
            for match in re.finditer(r"(?<!!)\[[^\]\n]+\]\(([^\s)]+)(?:\s+\"[^\"]*\")?\)",
                                     page.read_text(encoding="utf-8")):
                url = urlsplit(match[1].strip("<>"))
                if url.scheme:
                    continue
                target = (page.parent / unquote(url.path)).resolve() if url.path else page
                with self.subTest(page=page.relative_to(ROOT), target=match[1]):
                    self.assertTrue(target.is_relative_to(ROOT), "Local links must stay in the repository")
                    self.assertTrue(target.exists(), "Local link target is missing")
                    if url.fragment and target.suffix == ".md" and target != EXCLUDED:
                        self.assertIn(unquote(url.fragment), heading_anchors(target.read_text(encoding="utf-8")))

    def test_documented_commands_resolve_against_code(self):
        npm_scripts = json.loads(source("web/package.json"))["scripts"]
        pages = ("README.md", "AGENTS.md", "docs/PLUGIN_GUIDE.md", "docs/SAMPLES.md", "docs/RELEASING.md")
        for page in pages:
            text = document(page)
            for script in re.findall(r"\./(scripts/[\w-]+\.ps1)", text):
                with self.subTest(page=page, script=script):
                    self.assertIn(script, source_paths())
            for match in re.finditer(r"npm run ([\w-]+)([^;\n`]*)", text):
                with self.subTest(page=page, npm_script=match[1]):
                    self.assertTrue(match[1] in npm_scripts or "--if-present" in match[2])
            for project in re.findall(r"dotnet (?:test|run|build) (?:--project )?([^\s`]+)", text):
                with self.subTest(page=page, project=project):
                    if "<" in project or project.startswith("$") or project.startswith("plugins/ExampleGame/"):
                        continue  # Variables/templates and the intentionally new scaffold project.
                    self.assertTrue(project in source_paths() or any(
                        path.startswith(project + "/") and path.endswith(".csproj") for path in source_paths()),
                        "Documented project does not exist")

    def test_documented_reference_and_action_routes_exist(self):
        guide = document("docs/PLUGIN_GUIDE.md")
        for filename in ("ReferenceFieldEndpoints.cs", "ActionReviewEndpoints.cs", "DocumentGroupEndpoints.cs"):
            code = source(f"src/SaveToolkit.Api/Endpoints/{filename}")
            for method, route in re.findall(r'\.Map(Get|Post)\("(/[^"]+)"', code):
                with self.subTest(method=method, route=route):
                    self.assertIn(route, guide)

    def test_current_policy_has_no_superseded_publisher_refusal(self):
        policy = document("docs/PLUGIN_GUIDE.md").split("## Hard rules", 1)[1].split(
            "## Catalog-valued", 1)[0]
        self.assertIn("Owner decision, 2026-10-29", policy)
        # The decision is repository policy, not a claim about an upstream licence grant.
        for page in ("README.md", "docs/OWNER_CHECKLIST.md", "docs/PLUGIN_GUIDE.md"):
            with self.subTest(page=page):
                text = document(page)
                self.assertIn("2026-10-29", text)
                refusal = re.search(r"(?is)publisher\s+(?:artwork|art|assets|LOCA text|map art)"
                                    r"[^.;]{0,160}(?:remain[^.;]*refus|remain[^.;]*excluded|requires compatible permission)", text)
                self.assertIsNone(refusal, f"Superseded publisher refusal in {page}")
                self.assertNotRegex(text, r"(?is)does not permit\s+publisher")

    def test_scaffold_examples_create_an_unused_plugin(self):
        for page in ("AGENTS.md", "docs/PLUGIN_GUIDE.md"):
            text = document(page)
            names = re.findall(r"new-plugin\.ps1 -Name ([A-Z][A-Za-z0-9]+)", text)
            for name in names:
                with self.subTest(page=page, name=name):
                    self.assertFalse(any(path.startswith(f"plugins/{name}/") for path in source_paths()),
                                     "The scaffolder rejects an existing plugin folder")
                    self.assertIn(f"plugins/{name}/SaveToolkit.Plugins.{name}.Tests", text)

    def test_conformance_coverage_documents_source_limits(self):
        code = source("src/SaveToolkit.Testing/PluginConformanceTests.cs")
        text = document("docs/PLUGIN_GUIDE.md").split("## Tests", 1)[1].split("## Plugin README", 1)[0]
        for name in ("MaxFilesPerTitle", "LazyExpansionBudget"):
            value = re.search(rf"{name} => (\d+)", code)[1]
            self.assertRegex(text, rf"`{name}`[^\n]*\*\*{value}\*\*")
        value = re.search(r"DefaultQueryPageBudget = (\d+)", code)[1]
        self.assertRegex(text, rf"`QueryPageBudget`[^\n]*\*\*{value}\*\*")
        self.assertNotIn("opening every claimed sample", text)

    def test_plans_distinguish_the_original_baseline(self):
        for name, _, _ in plugins():
            with self.subTest(plugin=name):
                text = document(f"docs/plans/{name}.md")
                introduction = text.split("## Summary", 1)[0]
                self.assertIn("Historical planning snapshot", introduction)
                self.assertIn("../PLUGINS.md", introduction)
                self.assertNotIn("## Current state (evidence)", text)

    def test_reconnaissance_and_smoke_counts_are_historical(self):
        for path in sorted((ROOT / "docs/formats").glob("*.md")):
            with self.subTest(path=path.name):
                text = path.read_text(encoding="utf-8")
                self.assertIn("Historical reconnaissance", text.split("##", 1)[0])
                self.assertIn("../PLUGINS.md", text.split("##", 1)[0])
        introduction = document("docs/E2E_SMOKE_36.md").split("## Scope", 1)[0]
        self.assertIn("Historical run report", introduction)
        self.assertIn("#40", introduction)

    def test_sample_file_sizes_match_supported_layouts(self):
        text = document("docs/SAMPLES.md")
        ff7 = source("plugins/FinalFantasy7/SaveToolkit.Plugins.FinalFantasy7/Ff7Schema.cs")
        sizes = {"PPSA01911": int(re.search(r"RemakeFileLength = ([\d_]+)", ff7)[1].replace("_", "")),
                 "PPSA08664": int(re.search(r"RebirthFileLength = ([\d_]+)", ff7)[1].replace("_", ""))}
        fixture = source("plugins/EldenRing/SaveToolkit.Plugins.EldenRing.Tests/SyntheticSave.cs")
        sizes["PPSA03169"] = int(re.search(r"FileSize = (0x[\da-fA-F]+)", fixture)[1], 16)
        for title, size in sizes.items():
            with self.subTest(title=title):
                row = next(line for line in text.splitlines() if line.startswith(f"| {title} |"))
                self.assertIn(f"{size:,} bytes", row)

    def test_overview_qualifies_history_and_backup_guarantees(self):
        text = document("README.md")
        introduction = text.split("## Features", 1)[0]
        self.assertIn("history limits", introduction)
        self.assertIn("optional backup mirroring", introduction)
        self.assertIn("original bytes", introduction)
        self.assertIn("ownership", introduction)
        # These conditions exist in Core; the overview must not promise unconditional behavior.
        self.assertIn("MaxSnapshotBytes", source("src/SaveToolkit.Core/Sessions/EditHistory.cs"))
        exporter = source("src/SaveToolkit.Core/Export/WorkspaceExporter.cs")
        self.assertIn("if (options.MirrorBackups)", exporter)
        self.assertIn("protectedPaths", exporter)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--code-ref", default="HEAD", help="Git ref for implementation checks (default: HEAD)")
    args, unittest_args = parser.parse_known_args()
    CODE_REF = args.code_ref
    unittest.main(argv=[__file__, *unittest_args])
