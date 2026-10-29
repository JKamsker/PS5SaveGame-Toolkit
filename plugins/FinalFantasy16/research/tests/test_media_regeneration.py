import contextlib
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from tooling_support import (PACKS, checkout_bytes, copy_media_inputs, load_tool,
                             run_script, temporary_directory)


class MediaRegenerationTests(unittest.TestCase):
    def test_base_generation_then_media_matches_every_committed_output(self):
        with temporary_directory() as scratch:
            root = Path(scratch)
            packs = copy_media_inputs(root)
            # Remove outputs: decoration must consume freshly generated base
            # records/manifests, not merely reproduce already decorated inputs.
            outputs = [(pack.name, file.name) for pack in packs.iterdir()
                       for file in pack.glob("*.json")]
            for pack, name in outputs:
                (packs / pack / name).unlink()
            for name in ("build_published_catalog", "build_ability_catalog", "apply_game_assets"):
                result = run_script(root / "research" / (name + ".py"))
                self.assertEqual(0, result.returncode, result.stderr.decode("utf-8"))
                if name == "apply_game_assets":
                    self.assertEqual(b"Validated/decorated 145 media records; 398116 binary bytes.\n",
                                     result.stdout)
            self.assertEqual(9, len(outputs))
            for pack, name in outputs:
                with self.subTest(pack=pack, file=name):
                    self.assertEqual(checkout_bytes(PACKS / pack / name),
                                     (packs / pack / name).read_bytes())
            first = {(pack, name): (packs / pack / name).read_bytes() for pack, name in outputs}
            result = run_script(root / "research/apply_game_assets.py")
            self.assertEqual(0, result.returncode, result.stderr.decode("utf-8"))
            self.assertEqual(first, {(pack, name): (packs / pack / name).read_bytes()
                                    for pack, name in outputs})

    def test_asset_validation_diagnostics_and_precedence(self):
        tool = load_tool("apply_game_assets")
        metadata = json.loads(tool.METADATA.read_bytes())
        pack_id, rows = next(iter(metadata["records"].items()))
        original = rows[0]
        with temporary_directory() as scratch:
            pack = Path(scratch)
            content = (PACKS / pack_id / original["asset"]).read_bytes()
            (pack / original["asset"]).write_bytes(content)
            cases = [({"asset": "../bad.webp"}, "Invalid local asset path: ../bad.webp"),
                     ({"sha256": "bad", "width": 0},
                      f"Asset hash/length mismatch: {original['asset']}"),
                     ({"bytes": 0}, f"Asset hash/length mismatch: {original['asset']}"),
                     ({"height": 65}, f"Asset dimensions exceed the recorded budget: {original['asset']}")]
            for changes, message in cases:
                with self.subTest(changes=changes), self.assertRaises(ValueError) as caught:
                    tool.asset_file(pack, {**original, **changes})
                self.assertEqual(message, str(caught.exception))
            (pack / original["asset"]).write_bytes(b"invalid")
            with self.assertRaises(ValueError) as caught:
                tool.asset_file(pack, {**original, "sha256": "also invalid"})
            self.assertEqual(f"Invalid WebP: {original['asset']}", str(caught.exception))

    def test_name_mismatch_precedes_record_and_manifest_writes(self):
        tool = load_tool("apply_game_assets")
        with temporary_directory() as scratch:
            packs = copy_media_inputs(Path(scratch))
            metadata = json.loads(tool.METADATA.read_bytes())
            pack_id, rows = next(iter(metadata["records"].items()))
            rows[0]["name"] = "Wrong name"
            metadata_path = Path(scratch) / "metadata.json"
            metadata_path.write_text(json.dumps(metadata), encoding="utf-8")
            originals = {path: path.read_bytes() for path in (packs / pack_id).glob("*.json")}
            with patch.object(tool, "PACKS", packs), patch.object(tool, "METADATA", metadata_path):
                with self.assertRaises(ValueError) as caught:
                    tool.main()
            self.assertEqual(f"Name/ID mismatch: {pack_id}/{rows[0]['id']}", str(caught.exception))
            self.assertEqual(originals, {path: path.read_bytes() for path in originals})

    def test_total_budget_failure_occurs_after_pack_writes(self):
        tool = load_tool("apply_game_assets")
        with temporary_directory() as scratch:
            root = Path(scratch)
            pack = root / "ffxvi.maps.en"
            pack.mkdir()
            asset = pack / "map-large.webp"
            asset.write_bytes(b"x" * 4_000_001)
            metadata = {"retrieved": "2026-10-29", "records": {pack.name: [
                {"asset": asset.name, "id": "valisthea", "name": "World", "source": tool.WORLD_URL,
                 "width": 1, "height": 1}]}}
            metadata_path = root / "metadata.json"
            metadata_path.write_text(json.dumps(metadata), encoding="utf-8")
            # Isolate the aggregate budget from the separate per-image gate.
            with patch.object(tool, "PACKS", root), patch.object(tool, "METADATA", metadata_path), \
                    patch.object(tool, "asset_file", return_value={"path": asset.name, "sha256": "test"}), \
                    contextlib.redirect_stdout(io.StringIO()) as stdout:
                with self.assertRaises(ValueError) as caught:
                    tool.main()
            self.assertEqual("New media exceed the 4 MB plugin budget: 4000001", str(caught.exception))
            self.assertEqual("", stdout.getvalue())
            self.assertTrue((pack / "maps.json").exists())
            self.assertTrue((pack / "pack.json").exists())
