"""Replay the unavailable encoder boundary; exercise every downstream overlay byte."""

import contextlib
import io
import json
import shutil
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_assets
from importer_test_support import PACK, assert_pack_equal, no_network, pack_bytes, scratch_directory


def restore_text_only_pack(pack):
    """Remove v3 decorations to exercise application as well as idempotent reapplication.

    This is a test preimage, not a claim of regenerating the missing wiki responses.
    The committed files remain the independent, byte-for-byte expected result.
    """
    manifest = json.loads((pack / "pack.json").read_text(encoding="utf-8"))
    manifest["version"] = "2.0.0"
    manifest["datasets"] = [d for d in manifest["datasets"] if d["kind"] == "records"]
    for dataset in manifest["datasets"]:
        dataset["attribution"]["modifications"] = dataset["attribution"]["modifications"].replace(
            "originally omitted images; v3 adds separately attributed publisher media and official transcriptions (assets.source.md)",
            "omitted images")
        for file in dataset["files"]:
            path = pack / file["path"]
            data = json.loads(path.read_text(encoding="utf-8"))
            for record in data["records"]:
                record.pop("assetId", None)
                attrs = record["attributes"]
                if "descriptionSource" in attrs:
                    record["description"] = record["description"].split("\n", 2)[2]
                for key in ["iconRole", "iconSource", "descriptionSource"]:
                    attrs.pop(key, None)
            path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
            file["sha256"] = build_assets.digest(path.read_bytes())
    (pack / "pack.json").write_text(json.dumps(manifest), encoding="utf-8")
    for folder in ["icons", "maps"]:
        shutil.rmtree(pack / folder)


class AssetGoldenTests(unittest.TestCase):
    def setUp(self):
        self.scratch = Path(self.enterContext(scratch_directory()))
        self.enterContext(no_network())
        self.enterContext(patch("urllib.request.urlopen", side_effect=AssertionError("Network is forbidden")))
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))
        self.expected = pack_bytes(PACK, canonical_json_newlines=True)
        self.pack = self.scratch / "pack"
        shutil.copytree(PACK, self.pack)
        for name, content in self.expected.items():
            if name.endswith(".json"):
                (self.pack / name).write_bytes(content)
        self.enterContext(patch.object(build_assets, "PACK", self.pack))
        self.ledger = json.loads(self.expected["asset-sources.json"])

    def replay_encoder(self):
        # Only original-image decoding/encoding is replaced. The unchanged build must
        # still check encoded pins, deduplicate, budget, copy, join and serialize.
        assets = {a["sourceUrl"]: a for a in self.ledger["assets"]}

        def encoded(source, is_map):
            asset = assets[source.decode()]
            self.assertEqual(asset["role"] == "map", is_map)
            return self.expected[asset["path"]], Path(asset["path"]).suffix, (asset["width"], asset["height"])

        download = self.enterContext(patch.object(build_assets, "download", side_effect=lambda a, _: a["sourceUrl"].encode()))
        encoder = self.enterContext(patch.object(build_assets, "encode_image", side_effect=encoded))
        return download, encoder

    def test_full_overlay_matches_all_committed_pack_bytes_twice(self):
        restore_text_only_pack(self.pack)
        self.assertFalse((self.pack / "icons").exists())
        self.assertNotEqual(self.expected["items.json"], (self.pack / "items.json").read_bytes())
        download, encoder = self.replay_encoder()
        for _ in range(2):
            build_assets.build(self.scratch / "processing")
            assert_pack_equal(self, self.expected, self.pack)
        self.assertEqual(2 * len(self.ledger["assets"]), download.call_count)
        self.assertEqual(download.call_count, encoder.call_count)

    def test_changed_encoder_pin_is_rejected_before_pack_writes(self):
        self.replay_encoder()
        with patch.object(build_assets, "encode_image", return_value=(b"changed", ".webp", (1, 1))):
            with self.assertRaisesRegex(ValueError, "Encoder output changed"):
                build_assets.build(self.scratch / "processing")
        assert_pack_equal(self, self.expected, self.pack)

    def test_total_budget_rejection_does_not_write_pack(self):
        self.replay_encoder()
        with patch.object(build_assets, "ASSET_BUDGET", 0):
            with self.assertRaisesRegex(ValueError, "Asset overlay exceeds"):
                build_assets.build(self.scratch / "processing")
        assert_pack_equal(self, self.expected, self.pack)

    def test_cached_original_bytes_are_hash_checked_offline(self):
        cache = self.scratch / "downloads"
        cache.mkdir()
        content = b"authored source payload"
        asset = {"sourceUrl": "https://example.invalid/image.png", "originalSha256": build_assets.digest(content)}
        path = cache / (build_assets.digest(asset["sourceUrl"].encode()) + ".download")
        path.write_bytes(content)
        self.assertEqual(content, build_assets.download(asset, cache))
        path.write_bytes(b"corrupted")
        with self.assertRaisesRegex(ValueError, "Upstream image changed"):
            build_assets.download(asset, cache)

    def test_missing_original_is_never_downloaded_by_tests(self):
        with self.assertRaisesRegex(AssertionError, "Network is forbidden"):
            build_assets.download(self.ledger["assets"][0], self.scratch)

    def test_image_processing_rejects_repository_scratch(self):
        with self.assertRaisesRegex(ValueError, "Image processing must stay outside"):
            build_assets.build(build_assets.PLUGIN / "ReferenceData")


if __name__ == "__main__":
    unittest.main()
