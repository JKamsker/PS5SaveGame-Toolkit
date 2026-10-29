"""Sample-free regression checks for the documentation publication boundary and build inputs."""

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import re
import tempfile
import unittest
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[3]
SITE = ROOT / "docs-site"


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / f"scripts/docs/{name}.py")
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


schema_check = module("check_schema")
site_check = module("check_site")
prepare_site = module("prepare_site")
renderer = module("bundle_renderer")


def headings(text):
    titles = re.findall(r"(?m)^#{1,6}\s+(.+?)\s*#*\s*$", text)
    return {re.sub(r"[^\w\- ]", "", title.lower()).replace(" ", "-") for title in titles}


class PipelineTests(unittest.TestCase):
    def test_public_allowlist_covers_only_curated_sources(self):
        allowlist = json.loads((SITE / "public-files.json").read_text(encoding="utf-8"))
        self.assertEqual(len(allowlist), len(set(allowlist)))
        expected = {"docfx.json", "index.md", "toc.yml", "theme/public/main.css", "theme/public/main.js",
                    "assets/toolkit.svg", "assets/http-reference.js", "assets/redoc/logo-mini.svg", "assets/redoc/logo-mini-full.svg",
                    "assets/redoc/NOTICE.txt"}
        for folder in ("getting-started", "guides", "tutorials", "reference", "contributing"):
            expected.update(path.relative_to(SITE).as_posix() for path in (SITE / folder).iterdir()
                            if path.suffix in {".md", ".yml"})
        self.assertEqual(expected, set(allowlist))
        for name in allowlist:
            self.assertTrue((SITE / name).resolve().is_relative_to(SITE.resolve()))
            self.assertTrue((SITE / name).is_file())
        config = json.loads((SITE / "docfx.json").read_text(encoding="utf-8"))
        self.assertEqual(["default", "modern", "theme"], config["build"]["template"])
        self.assertNotIn("../", json.dumps(config))

    def test_authoritative_repository_links_and_anchors_exist(self):
        prefix = "https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/"
        count = 0
        for page in SITE.rglob("*.md"):
            for target in re.findall(r"\]\((https://[^\s)]+)\)", page.read_text(encoding="utf-8")):
                if not target.startswith(prefix):
                    continue
                url = urlsplit(target[len(prefix):])
                path = ROOT / unquote(url.path)
                with self.subTest(page=page.name, link=target):
                    self.assertTrue(path.is_file())
                    if url.fragment and path.suffix == ".md":
                        self.assertIn(unquote(url.fragment), headings(path.read_text(encoding="utf-8")))
                count += 1
        self.assertGreater(count, 20)

    def test_renderer_and_generator_are_pinned_and_local(self):
        manifest = json.loads((ROOT / ".config/dotnet-tools.json").read_text(encoding="utf-8"))
        self.assertEqual("2.81.0", manifest["tools"]["docfx"]["version"])
        for tool in ("dotnet-stryker", "dotnet-reportgenerator-globaltool"):
            self.assertIn(tool, manifest["tools"])
        package = json.loads((SITE / "package.json").read_text(encoding="utf-8"))
        lock = json.loads((SITE / "package-lock.json").read_text(encoding="utf-8"))
        version = package["dependencies"]["redoc"]
        self.assertEqual("3.0.1", version)
        self.assertEqual(version, lock["packages"]["node_modules/redoc"]["version"])
        page = (SITE / "reference/http.md").read_text(encoding="utf-8")
        initializer = (SITE / "assets/http-reference.js").read_text(encoding="utf-8")
        self.assertIn('disableTelemetry: true', initializer)
        self.assertIn("from './redoc/redoc.standalone.js'", initializer)
        self.assertIn('src="../assets/http-reference.js"', page)
        self.assertNotRegex(page, r"(?:src|spec-url)=\"https?://")

    def test_schema_guard_ignores_only_server_origin_and_never_repins(self):
        expected = json.loads(schema_check.CONTRACT.read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "openapi.json"
            changed_host = {**expected, "servers": [{"url": "http://127.0.0.1:12345"}]}
            path.write_text(json.dumps(changed_host), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                schema_check.check(path)
            actual = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("http://127.0.0.1:5180", actual.pop("servers")[0]["url"])
            self.assertEqual(expected, actual)
            actual["paths"].pop(next(iter(actual["paths"])))
            path.write_text(json.dumps(actual), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "differs from the reviewed contract"):
                schema_check.check(path)
        self.assertEqual(expected, json.loads(schema_check.CONTRACT.read_text(encoding="utf-8")))

    def test_renderer_bundles_attribution_locally_and_detects_upstream_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            stage = Path(directory) / "stage"
            package = Path(directory) / "package"
            (package / "bundles").mkdir(parents=True)
            prepare_site.prepare(stage)
            source = '"https://cdn.redoc.ly/redoc/logo-mini.svg", "https://cdn.redoc.ly/redoc/logo-mini-full.svg"'
            (package / "bundles/redoc.standalone.js").write_text(source, encoding="utf-8")
            (package / "LICENSE").write_text("MIT", encoding="utf-8")
            renderer.bundle(package, stage)
            output = (stage / "assets/redoc/redoc.standalone.js").read_text(encoding="utf-8")
            self.assertNotIn("https:", output)
            self.assertEqual(2, output.count("data:image/svg+xml;base64,"))
            (package / "bundles/redoc.standalone.js").write_text("changed", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Review the pinned upgrade"):
                renderer.bundle(package, stage)

    def test_stage_has_no_repository_data_and_uses_current_scaffold(self):
        with tempfile.TemporaryDirectory() as directory:
            stage = Path(directory) / "source"
            prepare_site.prepare(stage)
            self.assertFalse((stage / "artifacts").exists())
            self.assertFalse((stage / "web").exists())
            self.assertFalse((stage / "README.md").exists())
            self.assertFalse((stage / "docs/OWNER_ACTIONS.md").exists())
            self.assertTrue((stage / ".nojekyll").is_file())
            for kind in ("Plugin", "Document"):
                code = (stage / f"examples/ExampleGame{kind}.cs").read_text(encoding="utf-8")
                self.assertIn("SaveToolkit.Plugins.ExampleGame", code)
                self.assertNotIn("@NAME@", code)
                self.assertLess(len(code.splitlines()), 100)

    def test_relative_urls_resolve_at_root_and_project_base(self):
        site = Path(tempfile.gettempdir()) / "docs-output"
        for base in site_check.BASES:
            target, anchor = site_check.local_target(site, "tutorials/first-plugin.html", "../api/type.html#member", base)
            self.assertEqual((site / "api/type.html").resolve(), target)
            self.assertEqual("member", anchor)
        with self.assertRaisesRegex(ValueError, "escapes project base"):
            site_check.local_target(site, "index.html", "/assets/main.js", site_check.BASES[1])

    def test_fragment_parser_collects_targets_and_resource_urls(self):
        page = site_check.Page('<h2 id="example">Example</h2><a href="#example">Go</a>'
                               '<script src="main.js"></script><redoc spec-url="openapi.json"></redoc>')
        self.assertEqual({"example"}, page.anchors)
        self.assertEqual(["#example", "main.js", "openapi.json"], page.links)

    def test_pages_deployment_is_main_only_and_permissions_are_separate(self):
        workflow = (ROOT / ".github/workflows/docs.yml").read_text(encoding="utf-8")
        self.assertIn("pull_request:", workflow)
        self.assertNotIn("pull_request_target", workflow)
        self.assertIn("branches: [main]", workflow)
        build, deploy = workflow.split("\n  deploy:\n")
        gate = "if: github.ref == 'refs/heads/main' && github.event_name != 'pull_request'"
        self.assertIn(gate, build)
        self.assertIn(gate, deploy)
        self.assertIn("needs: build", deploy)
        self.assertIn("name: github-pages", deploy)
        self.assertIn("url: ${{ steps.deployment.outputs.page_url }}", deploy)
        self.assertIn("pages: write", deploy)
        self.assertIn("id-token: write", deploy)
        self.assertNotIn("pages: write", build)
        self.assertNotIn("id-token: write", build)
        self.assertNotIn("secrets.", workflow)
        self.assertIn("enablement: false", deploy)
        self.assertIn("path: ${{ env.DOCS_SITE }}", build)
        self.assertIn("scripts/build-docs.ps1", build)


if __name__ == "__main__":
    unittest.main()
