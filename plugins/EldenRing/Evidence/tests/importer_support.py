"""Offline harness: run the real importers with every output redirected to scratch."""

import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
from unittest.mock import patch
import zipfile

EVIDENCE = Path(__file__).resolve().parents[1]
PACKS = EVIDENCE.parent / "SaveToolkit.Plugins.EldenRing/ReferencePacks"
FIXTURES = Path(__file__).with_name("fixtures")
sys.dont_write_bytecode = True


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), EVIDENCE / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def snapshot(root):
    return {path.relative_to(root).as_posix(): sha256(path.read_bytes())
            for path in sorted(root.rglob("*")) if path.is_file()}


def run(module, scratch):
    with patch.object(sys, "argv", [module.__file__, str(scratch)]), contextlib.redirect_stdout(io.StringIO()):
        module.main()


def rust_inputs(root):
    with zipfile.ZipFile(FIXTURES / "clayamore-inputs.zip") as archive:
        archive.extractall(root)


def name_pack(root):
    module = load("import-name-catalog")
    rust_inputs(root / "inputs")
    module.OUTPUT = root / "names"
    run(module, root / "inputs/names")
    return module.OUTPUT


def progress_pack(root):
    module = load("import-progress-catalog")
    rust_inputs(root / "inputs")
    module.OUTPUT = root / "progress"
    run(module, root / "inputs/progress")
    return module.OUTPUT


def community_pack(root):
    # Tiny literal-only archives exercise the parser; these are not claimed as pinned upstream bytes.
    from synthetic_inputs import community_inputs
    module = load("import-community-catalog")
    scratch = root / "inputs"
    scratch.mkdir()
    archives, groups, blocks = community_inputs(module, scratch)
    module.S.ARCHIVES = archives
    module.GROUPS_HASH = sha256(groups)
    module.BST_HASH = sha256(blocks)
    module.OUTPUT = root / "plugin/ReferencePacks/eldenring.community.en"
    # Ensure the obsolete dump is removed, as on an actual v1.0 -> v1.1 regeneration.
    module.OUTPUT.mkdir(parents=True)
    (module.OUTPUT / "param-rows.json").write_text("obsolete", encoding="utf-8")
    # The original CLI exits after generation; close its archives explicitly in this in-process harness.
    original_sources = module.S.Sources
    opened = []

    def sources(path):
        result = original_sources(path)
        opened.append(result)
        return result

    with patch.object(module.S, "Sources", sources):
        try:
            run(module, scratch)
        finally:
            for source in opened:
                for archive in source.archives.values():
                    archive.close()
    return root / "plugin"


def visual_pack(root):
    from synthetic_inputs import visual_inputs
    module = load("import-visual-assets")
    module.ROOT = root
    module.PACK = name_pack(root)
    (root / "Evidence").mkdir()
    scratch = root / "visual-inputs"
    visual_inputs(module, scratch)
    run(module, scratch)
    return root


def visual_snapshot(root):
    # Include every pack file (even untouched name/licence sidecars) and the summary.
    return {**{"names/" + key: value for key, value in snapshot(root / "names").items()},
            "Evidence/visual-summary.json": sha256((root / "Evidence/visual-summary.json").read_bytes())}
