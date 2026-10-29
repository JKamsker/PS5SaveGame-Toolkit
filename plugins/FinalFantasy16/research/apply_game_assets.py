"""Apply reviewed game media to regenerated catalogues, offline and deterministically.

Run after build_published_catalog.py and build_ability_catalog.py with python -I.
All image decoding/resizing happens in a temporary directory before import. This
step only checks committed WebP hashes and decorates the original reference data.
"""
import hashlib
import json
from pathlib import Path

PLUGIN = Path(__file__).resolve().parents[1]
PACKS = PLUGIN / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks"
METADATA = PLUGIN / "research/game-assets.json"
SUFFIX = " Official game description"
WORLD_URL = "https://na.finalfantasyxvi.com/story"
REGION_URL = "https://game8.co/games/Final-Fantasy-XVI/archives/410003"


def write_json(path, value):
    content = (json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    path.write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def asset_file(pack, row):
    path = row["asset"]
    if "/" in path or "\\" in path or not path.endswith(".webp"):
        raise ValueError(f"Invalid local asset path: {path}")
    data = (pack / path).read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WEBP":
        raise ValueError(f"Invalid WebP: {path}")
    digest = hashlib.sha256(data).hexdigest()
    if digest != row["sha256"] or len(data) != row["bytes"]:
        raise ValueError(f"Asset hash/length mismatch: {path}")
    dimension_limit = 64 if path.startswith("icon-") else 2048
    if not all(0 < row[axis] <= dimension_limit for axis in ("width", "height")):
        raise ValueError(f"Asset dimensions exceed the recorded budget: {path}")
    if path.startswith("map-") and len(data) > 600_000:
        raise ValueError(f"Map exceeds the per-image byte budget: {path}")
    return {"path": path, "sha256": digest}


def attribution(source, retrieved, *, world=False):
    holder = "Square Enix" if world else "Square Enix and Game8"
    return {
        "sourceUrl": source, "author": holder,
        "license": f"Rights reserved by {holder}; used with attribution per owner decision 2026-10-29",
        "licenseUrl": WORLD_URL if world else "https://game8.co/",
        "retrieved": retrieved,
        "modifications": "Selected source images, resized preserving aspect ratio, encoded as WebP; metadata removed. "
                         "Image URLs and input/output hashes are in research/game-assets.json. "
                         "No save-state or coordinate mapping is established.",
    }


def write_map_pack(pack, pack_id, rows, files, metadata):
    records = [{"id": row["id"], "name": row["name"],
                "category": "world" if row["id"] == "valisthea" else "regional",
                "assetId": row["asset"], "source": row["source"],
                "description": "Reference artwork only; saved position/visited/completion alignment Unknown.",
                "attributes": {"width": str(row["width"]), "height": str(row["height"])}} for row in rows]
    digest = write_json(pack / "maps.json", {"records": records})
    datasets = [{"id": "ffxvi_map_art", "kind": "records", "title": "Map artwork references",
                 "files": [{"path": "maps.json", "sha256": digest}],
                 "attribution": attribution(REGION_URL, metadata["retrieved"])}]
    for key, world in [("world_art", True), ("regional_art", False)]:
        selected = [files[row["asset"]] for row in rows if (row["id"] == "valisthea") == world]
        datasets.append({"id": key, "kind": "assets", "title": key.replace("_", " "),
                         "files": selected,
                         "attribution": attribution(WORLD_URL if world else REGION_URL,
                                                    metadata["retrieved"], world=world)})
    write_json(pack / "pack.json", {
        "schema": 1, "id": pack_id, "version": "1.0.0", "title": "FFXVI reference map artwork",
        "plugins": ["ffxvi"], "titleIds": ["PPSA10665", "PPSA13803"], "gameBuilds": [],
        "locale": "en", "description": "World and regional reference art; no positioned save markers.",
        "datasets": datasets})


def decorate_records(records, rows, pack_id, metadata):
    by_id = {row["id"]: row for row in records["records"]}
    for row in rows:
        record = by_id[row["id"]]
        if record["name"] != row["name"]:
            raise ValueError(f"Name/ID mismatch: {pack_id}/{row['id']}")
        record["assetId"] = row["asset"]
        original = record["description"].split(SUFFIX, 1)[0]
        text = row.get("excerpt")
        if text:
            # One short excerpt per independently sourced record, never an upgrade/effect rule.
            record["description"] = original + SUFFIX + " (excerpt): “" + text + "” " + \
                "Square Enix game text, reproduced by Game8; " + row["source"] + \
                "; retrieved " + metadata["retrieved"] + ". " + metadata["license"] + "."


def decorate_manifest(manifest, pack_id, filename, digest, files, metadata):
    manifest["version"] = "1.1.0"
    manifest["description"] = "Attributed reference names and selected game icons/short official-description excerpts; " \
                              "unknown game-build coverage, no gameplay caps or save-index joins."
    for dataset in manifest["datasets"]:
        enriched = False
        for file in dataset["files"]:
            if file["path"] == filename:
                file["sha256"] = digest
                enriched = True
        if dataset["kind"] == "records":
            notice = " Selected records also carry attributed short Square Enix game-text excerpts from Game8; " \
                     "their media rights/source are recorded in each description and game_icons attribution."
            original = dataset["attribution"]["modifications"].split(notice)[0]
            dataset["attribution"]["modifications"] = original + (notice if enriched else "")
    manifest["datasets"] = [dataset for dataset in manifest["datasets"] if dataset["id"] != "game_icons"]
    source = "https://game8.co/games/Final-Fantasy-XVI/archives/" + ("415576" if "published" in pack_id else "409930")
    manifest["datasets"].append({"id": "game_icons", "kind": "assets", "title": "Game icons (Square Enix / Game8)",
                                 "files": [files[path] for path in sorted(files)],
                                 "attribution": attribution(source, metadata["retrieved"])})


def write_decorated_record_pack(pack, pack_id, rows, files, metadata):
    filename = "ffxvi_published_items.json" if pack_id == "ffxvi.published.en" else "abilities.json"
    records = json.loads((pack / filename).read_bytes())
    decorate_records(records, rows, pack_id, metadata)
    digest = write_json(pack / filename, records)
    manifest = json.loads((pack / "pack.json").read_bytes())
    decorate_manifest(manifest, pack_id, filename, digest, files, metadata)
    write_json(pack / "pack.json", manifest)


def main():
    metadata = json.loads(METADATA.read_bytes())
    total_bytes = 0
    for pack_id, rows in metadata["records"].items():
        pack = PACKS / pack_id
        files = {row["asset"]: asset_file(pack, row) for row in rows}
        total_bytes += sum((pack / path).stat().st_size for path in files)
        if pack_id == "ffxvi.maps.en":
            write_map_pack(pack, pack_id, rows, files, metadata)
        else:
            write_decorated_record_pack(pack, pack_id, rows, files, metadata)
    if total_bytes > 4_000_000:
        raise ValueError(f"New media exceed the 4 MB plugin budget: {total_bytes}")
    print(f"Validated/decorated {sum(map(len, metadata['records'].values()))} media records; {total_bytes} binary bytes.")


if __name__ == "__main__":
    main()
