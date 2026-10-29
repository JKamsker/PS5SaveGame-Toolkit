"""Read-only documentation checks; run with Python 3.10+ from any directory.

Use --code-ref main to compare working-tree documentation with local main.
No samples, network, third-party dependencies or generated files are needed.
"""

import argparse
from functools import lru_cache
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
                if url.scheme or not url.path:
                    continue
                target = (page.parent / unquote(url.path)).resolve()
                with self.subTest(page=page.relative_to(ROOT), target=match[1]):
                    self.assertTrue(target.is_relative_to(ROOT), "Local links must stay in the repository")
                    self.assertTrue(target.exists(), "Local link target is missing")

    def test_documented_reference_and_action_routes_exist(self):
        guide = document("docs/PLUGIN_GUIDE.md")
        for filename in ("ReferenceFieldEndpoints.cs", "ActionReviewEndpoints.cs", "DocumentGroupEndpoints.cs"):
            code = source(f"src/SaveToolkit.Api/Endpoints/{filename}")
            for method, route in re.findall(r'\.Map(Get|Post)\("(/[^"]+)"', code):
                with self.subTest(method=method, route=route):
                    self.assertIn(route, guide)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--code-ref", default="HEAD", help="Git ref for implementation checks (default: HEAD)")
    args, unittest_args = parser.parse_known_args()
    CODE_REF = args.code_ref
    unittest.main(argv=[__file__, *unittest_args])
