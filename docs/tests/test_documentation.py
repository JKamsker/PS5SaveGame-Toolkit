"""Read-only documentation checks; run with Python 3.10+ from any directory.

Use --code-ref main to compare working-tree documentation with local main.
No samples, network, third-party dependencies or generated files are needed.
"""

import argparse
from collections import Counter
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
FRAMEWORK_PAGES = ("README.md", "AGENTS.md", "docs/PLUGIN_GUIDE.md", "docs/RELEASING.md",
                   "docs/API.md", "docs/USER_GUIDE.md", "docs/SDK_REQUESTS.md", "docs/SAMPLES.md")


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


def script_parameters(path):
    # These scripts declare typed parameters before setting ErrorActionPreference.
    # Read declarations only: never execute PowerShell or inspect environment values.
    header = source(path).split("$ErrorActionPreference", 1)[0]
    if not re.search(r"(?m)^param\(", header):
        raise AssertionError(f"Expected a top-level parameter block: {path}")
    return set(re.findall(r"\]\s*\$(\w+)", header))


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


def markdown_section(text, heading):
    match = re.search(rf"(?m)^{re.escape(heading)}\s*$", text)
    if match is None:
        raise AssertionError(f"Missing Markdown section: {heading}")
    level = len(heading.split(" ", 1)[0])
    return re.split(rf"(?m)^#{{1,{level}}} ", text[match.end():], maxsplit=1)[0]


def markdown_table(text):
    rows = []
    for line in text.splitlines():
        if line.startswith("|"):
            cells = [cell.strip() for cell in re.split(r"(?<!\\)\|", line.strip()[1:-1])]
            if not all(re.fullmatch(r":?-{3,}:?", cell) for cell in cells):
                rows.append(cells)
    if not rows:
        raise AssertionError("Missing Markdown table")
    header, *data = rows
    if any(len(row) != len(header) for row in data):
        raise AssertionError("Inconsistent Markdown table column count")
    return header, data


def canonical_cell(cell, page):
    """Compare copied cells while allowing relative links to be rebased."""
    def canonical_link(match):
        url = urlsplit(match[2])
        if url.scheme:
            return match[0]
        target = ((ROOT / page).parent / unquote(url.path)).resolve()
        destination = target.relative_to(ROOT).as_posix()
        if url.fragment:
            destination += "#" + unquote(url.fragment)
        return match[1] + destination + ")"

    cell = re.sub(r"(\[[^\]\n]+\]\()([^\s)]+)\)", canonical_link, cell)
    return " ".join(cell.split())


def licence_rows(heading):
    header, rows = markdown_table(markdown_section(document("docs/LICENCE_EXCLUSIONS.md"), heading))
    if header != ["Plugin", "Source / revision", "Licence found", "Status",
                  "Disposition", "Alternative", "Unblock"]:
        raise AssertionError(f"Unexpected register columns in {heading}")
    return rows


class DocumentationTests(unittest.TestCase):
    def test_readme_licence_exclusions_match_register_in_both_directions(self):
        register = licence_rows("## README exclusion decisions")
        names = {name for name, _, _ in plugins()}
        self.assertEqual(names, {row[0] for row in register})
        for name in sorted(names):
            with self.subTest(plugin=name):
                page = f"plugins/{name}/README.md"
                header, expected = markdown_table(markdown_section(document(page), "## Licence exclusions"))
                self.assertIn(len(header), (4, 5))
                # Two legacy four-column tables omit a separate alternative.
                # Preserve their wording instead of silently dropping their rows.
                if len(header) == 4:
                    expected = [[*row[:3], "Not separately recorded in README.", row[3]] for row in expected]
                expected = [tuple(canonical_cell(cell, page) for cell in row) for row in expected]
                actual = [tuple(canonical_cell(cell, "docs/LICENCE_EXCLUSIONS.md")
                                for cell in [*row[1:3], *row[4:]])
                          for row in register if row[0] == name]
                self.assertCountEqual(expected, actual, "README/register source, revision or disposition drift")

    def test_additional_licence_decisions_have_readme_source_evidence(self):
        names = {name for name, _, _ in plugins()}
        for row in licence_rows("## Additional README source decisions"):
            name, source_cell = row[:2]
            with self.subTest(plugin=name, source=source_cell):
                self.assertIn(name, names)
                readme = document(f"plugins/{name}/README.md")
                urls = re.findall(r"\]\((https?://[^\s)]+)\)", source_cell)
                self.assertTrue(urls, "Additional decisions need concrete README source URLs")
                for url in urls:
                    self.assertIn(url, readme, "Historical source no longer appears in the README")

    def test_licence_register_summary_matches_decisions(self):
        sections = (licence_rows("## README exclusion decisions"),
                    licence_rows("## Additional README source decisions"))
        counts = Counter()
        for section_index, rows in enumerate(sections):
            for row in rows:
                with self.subTest(plugin=row[0], source=row[1]):
                    self.assertIn(row[3], ("Imported", "Still refused", "Not used"))
                counts[row[0], section_index] += 1
                counts[row[0], row[3]] += 1
        header, summary = markdown_table(markdown_section(document("docs/LICENCE_EXCLUSIONS.md"), "## Summary"))
        self.assertEqual(["Plugin", "README exclusions", "Additional decisions", "Imported",
                          "Still refused", "Not used", "Total"], header)
        names = {name for name, _, _ in plugins()}
        self.assertCountEqual([*sorted(names), "**Total**"], [row[0] for row in summary])
        for name, *values in summary:
            selected = names if name == "**Total**" else {name}
            expected = [sum(counts[plugin, key] for plugin in selected)
                        for key in (0, 1, "Imported", "Still refused", "Not used")]
            expected.append(sum(expected[:2]))
            with self.subTest(plugin=name):
                self.assertEqual(expected, [int(value) for value in values])

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
        for page in FRAMEWORK_PAGES:
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

    def test_documented_script_parameter_inventory_matches_declarations(self):
        section = markdown_section(document("docs/RELEASING.md"), "## Script parameters")
        _, rows = markdown_table(section)
        scripts = {path.rsplit("/", 1)[-1]: path for path in source_paths()
                   if re.fullmatch(r"scripts/[\w-]+\.ps1", path)}
        self.assertCountEqual(scripts, [row[0].strip("`") for row in rows])
        for name, parameters, _ in rows:
            path = scripts[name.strip("`")]
            with self.subTest(script=path):
                self.assertEqual(script_parameters(path), set(re.findall(r"`-(\w+)`", parameters)))

    def test_documented_script_invocations_use_real_parameters(self):
        invocations = 0
        for page in FRAMEWORK_PAGES:
            # Join PowerShell continuation lines before checking the invocation's flags.
            text = re.sub(r"`\r?\n\s*", " ", document(page))
            for match in re.finditer(r"\./(scripts/[\w-]+\.ps1)([^\n]*)", text):
                path, arguments = match.groups()
                invocations += 1
                declared = {name.lower() for name in script_parameters(path)}
                flags = re.findall(r"(?<!\S)-([A-Za-z]\w*)(?=[:\s`]|$)", arguments)
                with self.subTest(page=page, script=path, flags=flags):
                    self.assertLessEqual({name.lower() for name in flags}, declared)
        self.assertGreater(invocations, 10, "Script invocation scan unexpectedly found too few examples")

    def test_release_job_inventory_matches_all_workflow_keys(self):
        expected = set()
        for path in source_paths():
            if re.fullmatch(r"\.github/workflows/[\w-]+\.ya?ml", path):
                jobs = source(path).split("\njobs:\n", 1)[1]
                expected.update((path, job) for job in re.findall(r"(?m)^  ([\w-]+):\s*$", jobs))
        section = markdown_section(document("docs/RELEASING.md"), "## Continuous integration")
        actual = re.findall(r"(?m)^\| \[[^\]]+\]\(\.\./([^\s)]+)\) \| `([\w-]+)` \|", section)
        self.assertCountEqual(expected, actual)

    def test_coverage_platforms_and_nightly_fuzz_match_workflows(self):
        workflow = source(".github/workflows/ci.yml")
        self.assertIn("-Coverage:($IsWindows)", workflow)
        self.assertIn("if ($IsLinux)", workflow)
        self.assertIn("--coverage.reportsDirectory=", workflow)
        releasing = document("docs/RELEASING.md")
        self.assertIn(".NET coverage on Windows", releasing)
        self.assertIn("web coverage", releasing)
        nightly = source(".github/workflows/nightly-fuzz.yml")
        self.assertIn("cron: '23 3 * * *'", nightly)
        self.assertIn("03:23 UTC", releasing)
        self.assertIn("default: '5000'", nightly)
        self.assertIn("5,000 mutations per seed", releasing)
        self.assertIn("SAVETOOLKIT_FUZZ_TIME_BUDGET: '60'", nightly)
        self.assertIn("60-second per-plugin wall-clock cap", releasing)
        self.assertIn("timeout-minutes: 30", nightly)
        self.assertIn("30-minute job timeout", releasing)
        self.assertIn("--filter-method '*MutationFuzz'", nightly)
        self.assertNotIn("--coverage", nightly)
        options = source("src/SaveToolkit.Testing/MutationFuzzOptions.cs")
        report = source("src/SaveToolkit.Testing/MutationFuzzReport.cs")
        for variable in re.findall(r"`(SAVETOOLKIT_FUZZ_\w+)`", releasing):
            self.assertIn(variable, options + report)
        seeds = source("src/SaveToolkit.Testing/MutationFuzzSyntheticSeeds.cs")
        self.assertEqual(["format.json", "format.json-xor"], re.findall(r'case "([^"]+)":', seeds))
        self.assertEqual(3, seeds.count("yield return MemorySaveSet.SingleFile("))
        self.assertIn("other 15 plugin conformance classes", releasing)

    def test_built_app_smoke_docs_cover_shipped_flows_and_skipped_regressions(self):
        text = markdown_section(document("docs/RELEASING.md"), "## Built app browser smoke tests")
        flows = {
            "import.e2e.ts": ("import errors and recovery", "raw read-only inspection"),
            "workspace.e2e.ts": ("document isolation and redo", "repeated exports"),
            "table.e2e.ts": ("sort/paging",),
            "actions.e2e.ts": ("bulk preview/cancel/apply",),
            "keyboard.e2e.ts": ("keyboard-only application interaction",),
        }
        for filename, claims in flows.items():
            with self.subTest(file=filename):
                self.assertIn(f"web/e2e/{filename}", source_paths())
                for claim in claims:
                    self.assertIn(claim, text)
        for spec in ("import.e2e.ts", "table.e2e.ts"):
            with self.subTest(spec=spec):
                self.assertNotIn("test.fixme(", source(f"web/e2e/{spec}"))
        self.assertIn('test("unknown-layout export preserves the imported path', source("web/e2e/import.e2e.ts"))
        self.assertIn('test("clearing a JSON-type filter restores all rows', source("web/e2e/table.e2e.ts"))
        self.assertIn("No browser case is currently skipped", text)
        for page in ("README.md", "docs/API.md", "docs/USER_GUIDE.md"):
            with self.subTest(page=page):
                self.assertNotIn("acquire an `UNKNOWN/` prefix", document(page))
                self.assertNotIn("path-preservation gap", document(page))

    def test_user_guide_registered_titles_and_maturity_match_code(self):
        text = document("docs/USER_GUIDE.md")
        for name, plugin_id, maturity in plugins():
            rows = [line for line in text.splitlines()
                    if line.startswith("| ") and f"plugins/{name}/README.md)" in line]
            with self.subTest(plugin=name):
                self.assertTrue(rows, "Missing user-guide support row")
                for row in rows:
                    self.assertIn(f"`{plugin_id}`", row)
                    self.assertEqual(maturity, row.split("|")[4].strip())
                code = source(f"plugins/{name}/SaveToolkit.Plugins.{name}/{name}Plugin.cs")
                titles = set(re.findall(r'"(PPSA\d{5})"', code.split("Maturity =", 1)[0]))
                if titles:  # Generic format overlays intentionally are not registered titles.
                    self.assertEqual(titles, set(re.findall(r"PPSA\d{5}", "\n".join(rows))))

    def test_edit_coverage_status_ids_exist_in_plugin_source(self):
        section = markdown_section(document("docs/EDIT_COVERAGE.md"), "## Implementation status")
        header, rows = markdown_table(section.split("\n### ", 1)[0])
        self.assertEqual(["Rank", "Plugin", "Action/field ID", "Merge commit", "Tests",
                          "Known issues", "Remaining #31 owner validation"], header)
        self.assertEqual(list(range(1, 16)), [int(row[0]) for row in rows])
        literals = {}
        for path in sorted(source_paths()):
            match = re.fullmatch(r"plugins/([^/]+)/SaveToolkit\.Plugins\.\1/.+\.cs", path)
            if match:
                # Production C# only: a README or test expectation cannot establish an ID.
                literals.setdefault(match[1], set()).update(re.findall(r'"([^"\r\n]*)"', source(path)))
        for rank, plugin, ids_cell, *_ in rows:
            ids = re.findall(r"`([^`\n]+)`", ids_cell)
            with self.subTest(rank=rank, plugin=plugin):
                self.assertIn(plugin, literals)
                self.assertTrue(ids, "Each rank must document at least one action/field ID")
            for identifier in ids:
                with self.subTest(rank=rank, plugin=plugin, identifier=identifier):
                    self.assertIn(identifier, literals.get(plugin, set()),
                                  f"Documented ID missing from {CODE_REF} plugin source")

    def test_field_discovery_commits_are_on_main_and_handoffs_are_well_formed(self):
        section = markdown_section(document("docs/EDIT_COVERAGE.md"), "### Field discovery status")
        header, rows = markdown_table(section)
        self.assertEqual(["Plugin", "Field / outcome", "Confidence", "Merge commit",
                          "Blocker for writes", "Owner capture needed (#31)"], header)
        self.assertEqual(7, len(rows), "Keep one row per research outcome")
        repository = "https://github.com/JKamsker/PS5SaveGame-Toolkit"
        comment_pattern = re.escape(repository) + r"/issues/40#issuecomment-[1-9][0-9]*"
        commits = set()
        for row in rows:
            links = re.findall(r"\[[^\]\n]+\]\(([^\s)]+)\)", " ".join(row))
            comments = [url for url in links if "issuecomment" in url or "/issues/comments/" in url]
            with self.subTest(plugin=row[0]):
                self.assertEqual(1, len(comments), "Each outcome needs its #40 handoff")
                for url in comments:
                    self.assertRegex(url, rf"\A{comment_pattern}\Z")
                self.assertIn(repository + "/commit/", row[3], "Each outcome needs its merge commit")
        # Include the status snapshot and any implementation/evidence commits, not just merges.
        for url in re.findall(r"\[[^\]\n]+\]\(([^\s)]+)\)", section):
            if "/commit/" in url:
                self.assertRegex(url, re.escape(repository) + r"/commit/[0-9a-f]{7,40}\Z")
                commits.add(url.rsplit("/", 1)[1])
            if "issuecomment" in url or "/issues/comments/" in url:
                self.assertRegex(url, rf"\A{comment_pattern}\Z")
        self.assertTrue(commits, "Field discovery must cite its merged evidence")
        for commit in sorted(commits):
            with self.subTest(commit=commit):
                result = subprocess.run(["git", "merge-base", "--is-ancestor", commit, "main"],
                                        cwd=ROOT, capture_output=True, text=True, check=False)
                self.assertEqual(0, result.returncode, f"{commit} is not an ancestor of main: {result.stderr}")

    def test_edit_coverage_inventory_counts_match_updated_demand_rows(self):
        inventory = markdown_section(document("docs/EDIT_COVERAGE.md"), "## Per-plugin inventory")
        _, rows = markdown_table(inventory.split("\n### ", 1)[0])
        totals = Counter()
        for plugin, enabled, read_only, not_located, gaps in rows:
            section = markdown_section(inventory, next(
                line for line in inventory.splitlines() if line.startswith(f"### {plugin} — ")))
            counts = Counter(re.findall(r"(?m)^\| [A-Z]+[0-9]+ \| [^|]+ \| ([ERN]) —", section))
            with self.subTest(plugin=plugin):
                self.assertEqual([counts[code] for code in "ERN"],
                                 [int(enabled), int(read_only), int(not_located)])
                self.assertEqual(counts["R"] + counts["N"], int(gaps))
            totals.update(counts)
        summary = re.search(r"Across \*\*(\d+) demand groups\*\* after field discovery: "
                            r"\*\*(\d+) enabled\*\*, \*\*(\d+) read-only\s+with evidence\*\* and\s+"
                            r"\*\*(\d+) not located\*\*, giving \*\*(\d+) gaps\*\*", inventory)
        self.assertIsNotNone(summary)
        self.assertEqual([sum(totals.values()), *[totals[code] for code in "ERN"], totals["R"] + totals["N"]],
                         [int(value) for value in summary.groups()])

    def test_documented_sdk_helpers_and_storage_signatures_exist(self):
        guide = document("docs/PLUGIN_GUIDE.md")
        helpers = {
            "Collections/InMemoryCollection.cs": ("InMemoryCollection", "Query"),
            "ActionPlans.cs": ("ContentEquals", "Revalidate", "ApplyScalar"),
            "Binary/BoundedBinaryCursor.cs": ("BoundedBinaryCursor", "ReadCount", "Subrange"),
            "Binary/FStringCodec.cs": ("Read", "ByteLength", "Decode", "Encode"),
            "Binary/Crc32Ieee.cs": ("Compute", "Append", "GetCurrentHash", "Reset"),
            "References/ReferenceCatalogReader.cs": ("ReadBounded",),
            "References/ReferenceViews.cs": ("Icon",),
            "References/ReferenceInspection.cs": ("Create", "ResolveSelection"),
        }
        for path, members in helpers.items():
            code = source("src/SaveToolkit.Sdk/" + path)
            for member in members:
                with self.subTest(path=path, member=member):
                    self.assertRegex(code, rf"\b{member}\s*\(")
                    self.assertIn(member, guide)
        code = source("src/SaveToolkit.Sdk/BoundSaveDocument.cs")
        signature = (r"protected void RegisterField\(\s*string valueId,\s*FieldSpec spec,\s*"
                     r"Func<FieldValue> get,\s*Action<FieldValue>\? set = null,\s*"
                     r"Func<FieldValue, string\?>\? validate = null\)")
        self.assertRegex(code, signature)
        self.assertIn("protected FieldNode Field(string valueId, string label)", code)
        self.assertIn("RegisterField(valueId, spec, get, set = null, validate = null)", guide)
        self.assertIn("Storage-only `RegisterField` is also shipped", guide)
        self.assertTrue(any(path.endswith("/AllocationProbe.cs") for path in source_paths()))
        probe = source("src/SaveToolkit.Testing/AllocationProbe.cs")
        for member in ("Measure", "MeasureOpen", "MeasureProcessWideAsync", "DefaultMeasuredRuns"):
            with self.subTest(probe=member):
                self.assertIn(member, probe)
                self.assertIn(member, guide)
        self.assertIn("AllocationTestCollection", guide)
        options = source("src/SaveToolkit.Sdk/References/ReferenceIconOptions.cs")
        for member in ("ExactAltText", "RequireBytesAndProvenance", "RequireProvenance", "AssetIdOnly"):
            with self.subTest(icon_option=member):
                self.assertIn(member, options)
                self.assertIn(member, guide)
        self.assertIn("ReferenceIconOptions? options)", source("src/SaveToolkit.Sdk/References/ReferenceViews.cs"))

    def test_collection_query_docs_explain_cleared_values(self):
        code = source("src/SaveToolkit.Api/Endpoints/WorkspaceEndpoints.cs")
        self.assertIn('request.Query["sort"].FirstOrDefault() is { Length: > 0 } sort ? sort : null', code)
        self.assertIn("filter.Value.Length == 0", code)
        self.assertIn("filters.Remove(key)", code)
        text = document("docs/API.md")
        for claim in ("empty-string values mean no filter", "an empty string clears it",
                      "an empty string uses default ordering", "Whitespace-only values remain literal filters"):
            self.assertIn(claim, text)
        self.assertIn("Clear a column filter to", document("docs/USER_GUIDE.md"))
        self.assertNotIn("reload the page to reset the filter", document("docs/USER_GUIDE.md"))

    def test_sdk_02_to_05_documented_types_and_members_exist(self):
        # Check code spans, not arbitrary prose/path globs such as DocumentMember*.
        type_pattern = (r"EmbeddedReference(?:Context|PackLimits|PackException)|DocumentMember\w+|"
                        r"IFieldMetadataDocument|FieldMetadata\w*|FieldBindingKind|FieldSpecMetadata|"
                        r"ReferenceIcon(?:Options|Availability)")
        declarations = {}
        for path in sorted(source_paths()):
            if path.startswith("src/SaveToolkit.Sdk/") and re.fullmatch(type_pattern, Path(path).stem):
                code = source(path)
                for name in re.findall(r"public (?:sealed |static )?(?:class|record|interface|enum) (\w+)", code):
                    declarations[name] = code
        checked = set()
        for page in ("docs/PLUGIN_GUIDE.md", "docs/API.md", "docs/SDK_REQUESTS.md"):
            for span in re.findall(r"(?<!`)`([^`\n]+)`(?!`)", document(page)):
                if span.startswith(("src/", "tests/", "plugins/", "docs/")):
                    continue  # Evidence paths can name internal types and are not SDK member references.
                for match in re.finditer(rf"\b({type_pattern})\b(?:\.(\w+))?", span):
                    name, member = match.groups()
                    with self.subTest(page=page, type=name, member=member):
                        self.assertTrue(name in declarations, f"Missing public SDK type: {name}")
                        if member:
                            # Methods/properties/constants must be public; enum values are implicit public.
                            declaration = (rf"(?m)(?:public [^;{{\n]*\b{member}\s*(?:\(|\{{|=>|=)|"
                                           rf"^\s*{member}\s*[,=])")
                            self.assertIsNotNone(re.search(declaration, declarations[name]),
                                                 f"Missing public SDK member: {name}.{member}")
                    checked.add((name, member))
        self.assertIn(("EmbeddedReferenceContext", "Load"), checked)
        self.assertIn(("DocumentMemberRouter", "Resolve"), checked)
        self.assertIn(("FieldMetadataLimits", "MaxBatchSize"), checked)

    def test_sdk_review_and_metadata_limits_match_main(self):
        guide = document("docs/PLUGIN_GUIDE.md")
        api = document("docs/API.md")
        options = source("src/SaveToolkit.Core/Sessions/EditHistory.cs")
        for name, description in (("MaxReviewQueryRows", "rows"), ("MaxReviewQueryPages", "pages")):
            value = int(re.search(rf"{name}.*?= ([\d_]+);", options)[1].replace("_", ""))
            for page in (guide, api):
                with self.subTest(option=name, page="guide" if page == guide else "api"):
                    self.assertIn(name, page)
                    self.assertRegex(page, rf"{value:,} (?:query )?{description}")
        limit = re.search(r"MaxBatchSize = (\d+)", source("src/SaveToolkit.Sdk/FieldMetadataLimits.cs"))[1]
        self.assertIn(f"`FieldMetadataLimits.MaxBatchSize` is {limit}", guide)
        self.assertIn("public long MaxReviewQueryBytes { get; init; } = 16L * 1024 * 1024;", options)
        for page in (guide, api):
            self.assertIn("16 MiB", page)

    def test_sdk_adoption_status_matches_main_plugin_evidence(self):
        section = markdown_section(document("docs/PLUGIN_GUIDE.md"), "### SDK-01–05 adoption on main")
        _, rows = markdown_table(section)
        expected = {
            ("SDK-02", "Pragmata"): ("F6", "IFieldMetadataDocument"),
            ("SDK-03", "FinalFantasy7"): ("F2", "EmbeddedReferenceContext"),
            ("SDK-03", "FinalFantasy16"): ("F2", "EmbeddedReferenceContext"),
            ("SDK-03", "GodOfWarRagnarok"): ("F2", "EmbeddedReferenceContext"),
            ("SDK-04", "Gearbox"): ("F7", "DocumentMemberAdapter"),
            ("SDK-04", "XorJson"): ("F7", "DocumentMemberAdapter"),
            ("SDK-05", "BaldursGate3"): ("F3", "ReferenceViews.Icon"),
            ("SDK-05", "TalesOfArise"): ("F2", "ReferenceViews.Icon"),
        }
        actual = []
        for sdk, plugin_link, status, _ in rows:
            name = re.fullmatch(r"\[([^]]+)\]\(.+\)", plugin_link)[1]
            actual.append((sdk, name))
            with self.subTest(sdk=sdk, plugin=name):
                feature, helper = expected[(sdk, name)]
                self.assertEqual("Used", status)
                readme = source(f"plugins/{name}/README.md")
                adoption = markdown_section(readme, "## Framework feature adoption")
                feature_row = next(line for line in adoption.splitlines() if line.startswith(f"| {feature} "))
                self.assertIn("Used", feature_row)
                self.assertIn(helper, readme)
                usage = subprocess.run(["git", "grep", "--quiet", "--fixed-strings", helper, CODE_REF,
                    "--", f"plugins/{name}/SaveToolkit.Plugins.{name}/*.cs"], cwd=ROOT, check=False)
                self.assertEqual(0, usage.returncode, f"{helper} is not used in {CODE_REF} production code")
        self.assertCountEqual(expected, actual)

    def test_reference_field_route_lists_and_batch_bound_match_main(self):
        code = source("src/SaveToolkit.Api/Endpoints/ReferenceFieldEndpoints.cs")
        routes = {(method.upper(), route) for method, route in re.findall(r'\.Map(Get|Post)\("([^"]+)"', code)}
        guide = document("docs/PLUGIN_GUIDE.md")
        listed = set(re.findall(r"`(GET|POST) \.\.\.(/reference-fields/[\w-]+)", guide))
        self.assertEqual(routes, listed)
        api = document("docs/API.md")
        for method, route in routes:
            heading = f"### `{method} /api/workspaces/{{workspaceId}}/documents/{{documentId}}{route}`"
            self.assertEqual(1, api.count(heading))
        limit = re.search(r"MaxPairs = (\d+)", source("src/SaveToolkit.Api/Contracts/ReferenceBatchDtos.cs"))[1]
        batch = markdown_section(api,
            "### `POST /api/workspaces/{workspaceId}/documents/{documentId}/reference-fields/records`")
        self.assertIn(f"at most {limit} pairs", guide)
        self.assertIn(f"**{limit} pairs**", batch)
        self.assertIn("**413**", batch)
        self.assertIn("StatusCodes.Status413PayloadTooLarge",
                      source("src/SaveToolkit.Api/Infrastructure/ApiRequestValidation.cs"))

    def test_selection_table_binding_matches_the_shipped_working_tree_contract(self):
        contract = markdown_section(document("docs/PLUGIN_GUIDE.md"),
                                    "### Selection-table binding and advisory conformance (#40)")
        # A feature branch can ship additive APIs ahead of --code-ref's plugin inventory snapshot.
        sdk = document("src/SaveToolkit.Sdk/Views/ViewNode.cs")
        for name in ("SelectionTableId", "SelectionId"):
            self.assertIn(name, contract)
            self.assertIn(name, sdk)
        for term in ("NotEvaluated", "PermittedSharedValueIds", "PermittedDerivedValueIds",
                     "ExpectedChangedValueIds", "RowOwnedValueIds", "SelectionScopeFixture"):
            self.assertIn(term, contract)
        self.assertIn("no automatic selection-scope conformance fact", contract)
        self.assertIn("document scope", contract)

    def test_group_proposal_status_documents_empty_success_and_offer_errors(self):
        code = source("src/SaveToolkit.Core/Sessions/Groups/DocumentGroupRegistry.cs")
        self.assertIn("if (plugin is not IDocumentGroupPlugin groupPlugin) return [];", code)
        section = markdown_section(document("docs/API.md"),
            "### `GET /api/workspaces/{workspaceId}/files/{fileId}/group-proposals`")
        text = " ".join(section.split())
        self.assertIn("200 with an empty array", text)
        self.assertIn("offer's `errors`", text)
        self.assertIn("listing still returns 200", text)

    def test_quick_start_and_user_guide_match_packaged_launchers(self):
        package = source("scripts/package.ps1")
        launch_lines = [line for line in package.splitlines() if "--contentRoot" in line]
        self.assertEqual(2, len(launch_lines))
        for line in launch_lines:
            self.assertIn("--urls http://127.0.0.1:5180", line)
            self.assertIn("--Toolkit:DataRoot", line)
            self.assertIn("--Toolkit:PackRoot", line)
            self.assertNotIn("%*", line)
            self.assertNotIn('$@', line)
        for page in ("README.md", "docs/USER_GUIDE.md"):
            with self.subTest(page=page):
                text = document(page)
                for claim in ("http://127.0.0.1:5180", "start.cmd", "./start.sh",
                              "chmod +x start.sh SaveToolkit.Api", "SaveToolkit-<version>-<runtime>"):
                    self.assertIn(claim, text)
                self.assertIn("use-another-port", text)
        user = " ".join(document("docs/USER_GUIDE.md").split())
        count = re.search(r"\*\*(\d+) game/format plugins\*\*", user)
        self.assertIsNotNone(count)
        self.assertEqual(len(list(plugins())), int(count[1]))

    def test_mutation_fuzz_documents_current_defaults_and_scaffold_skips(self):
        guide = document("docs/PLUGIN_GUIDE.md")
        section = markdown_section(guide, "### Deterministic mutation fuzzing (#40)")
        code = source("src/SaveToolkit.Testing/PluginConformanceTests.MutationFuzz.cs")
        count = re.search(r"MutationFuzzCount => (\d+)", code)[1]
        cap = re.search(r"MaxMutationFuzzFilesPerTitle => (\d+)", code)[1]
        timeout = re.search(r"MutationFuzzCaseTimeout => TimeSpan.FromSeconds\((\d+)\)", code)[1]
        seed = re.search(r"DefaultSeed = (0x[0-9A-F]+)", source("src/SaveToolkit.Testing/SaveMutation.cs"))[1]
        for claim in (f"**{count} mutations per selected file**", f"(**{cap}**)",
                      f"**{timeout} seconds**", f"**`{seed}`**", "64 MiB + 32", "**1 GiB**"):
            self.assertIn(claim, section)
        self.assertIn("Assert.SkipWhen(result.Samples == 0", code)
        self.assertIn("With neither samples nor synthetic", section)
        scaffold = markdown_section(guide, "## Scaffold")
        self.assertIn("inherited `MutationFuzz` fact", scaffold)
        self.assertNotIn("Four sample-dependent tests", scaffold)

    def test_owner_policies_link_to_the_canonical_rules(self):
        guide = document("docs/PLUGIN_GUIDE.md")
        for notice in (
            "No licence published; reused with attribution per owner decision 2026-10-28",
            "Rights reserved by <holder>; used with attribution per owner decision 2026-10-29",
        ):
            self.assertIn(notice, guide)
        for page in ("README.md", "AGENTS.md", "docs/LICENCE_EXCLUSIONS.md"):
            with self.subTest(page=page):
                self.assertIn("PLUGIN_GUIDE.md#hard-rules", document(page))
        policy = markdown_section(document("AGENTS.md"), "## Collaboration policy")
        self.assertIn("security and vulnerability", policy)
        self.assertIn("out of scope by owner rule", policy)
        self.assertIn("RELEASING.md#disk-and-scratch", document("AGENTS.md"))
        self.assertIn("RELEASING.md#disk-and-scratch", guide)

    def test_sdk_request_totals_match_inventory_and_resolved_appendix(self):
        text = document("docs/SDK_REQUESTS.md")
        _, rows = markdown_table(markdown_section(text, "## Prioritized inventory"))
        counts = Counter(row[3] for row in rows)
        summary = re.search(r"(\d+) deduplicated items: (\d+) open, (\d+) partially resolved, "
                            r"(\d+) resolved, (\d+) obsolete", text)
        self.assertIsNotNone(summary)
        self.assertEqual([len(rows), *[counts[status] for status in
                         ("open", "partially resolved", "resolved", "obsolete")]],
                         [int(value) for value in summary.groups()])
        appendix = re.search(r"The (\d+) resolved rows", text)
        self.assertIsNotNone(appendix)
        self.assertEqual(counts["resolved"], int(appendix[1]))

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
                self.assertIn("**Status: superseded.**", introduction)
                self.assertIn("../PLUGINS.md", introduction)
                self.assertNotIn("## Current state (evidence)", text)
        proposal = document("docs/plans/F13-FIELD-SPEC-PUBLISHING.md")
        self.assertIn("partially delivered proposal", proposal)
        self.assertIn("That part of the proposal is done", proposal)
        self.assertIn("remain proposed", proposal)
        roadmap = document("docs/plans/ROADMAP.md").split("## Original plan", 1)[0]
        self.assertIn("F1–F14 are implemented", roadmap)
        self.assertNotIn("Yōtei remains read-only", roadmap)

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
        self.assertIn("Unknown-layout exports keep their imported paths", introduction)
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
