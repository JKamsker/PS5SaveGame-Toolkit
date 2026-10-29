"""Import reviewed, hash-pinned Compass data; never download or execute upstream code.

Run with python -I import-visual-assets.py <scratch>. All image processing stays
in scratch; only validated, size-bounded final WebP files enter ReferencePacks.
Requires Pillow. visual-inputs.json pins every consumed upstream byte.
"""
import hashlib
import io
import json
import pathlib
import re
import sys
from collections import Counter, defaultdict

from PIL import Image

REVISION = "be49e96b0513d8e08d22806b5b3fee9654e3e9de"
SOURCE = f"https://github.com/EthanShoeDev/elden-ring-compass/tree/{REVISION}"
RIGHTS = "Rights reserved by FromSoftware / Bandai Namco; used with attribution per owner decision 2026-10-29"
ROOT = pathlib.Path(__file__).resolve().parents[1]
PACK = ROOT / "SaveToolkit.Plugins.EldenRing/ReferencePacks/eldenring.names.en"
TABLES = {"weapon-names": "weapons", "item-names": "goods", "accessory-names": "talismans",
          "armor-names": "armor", "aow-names": "ashes-of-war"}
ARMOR = {"Bandit Mask", "Blue Cloth Cowl", "Blue Cloth Vest", "Warrior Gauntlets", "Warrior Greaves",
         "Astrologer Hood", "Astrologer Robe", "Vagabond Knight Helm", "Vagabond Knight Armor"}
GOODS = {"Consumable", "Crafting Material", "Upgrade Material", "Crafting Tool", "Key Item",
         "Crystal Tear", "Great Rune", "Wondrous Physick"}
# Reviewed punctuation difference on the sample's exact goods ID; neither identity is rewritten.
NAME_ALIASES = {("goods", 8600): ("Map: Limgrave- West", "Map: Limgrave, West")}
UNCERTAINTY = ("Upstream identity label only; Inferred on PS5. Includes unused/test/unnamed rows. "
               "No ownership, legal cap, upgrade compatibility or game-build coverage is asserted.")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8", newline="\n")


def array(scratch, name):
    text = (scratch / (name + ".ts")).read_text(encoding="utf-8")
    # Generated TS contains JSON literals, not executable expressions.
    start = text.index("[", text.index(" = "))
    return json.loads(re.sub(r",\s*]$", "]", text[start:text.rindex("]") + 1]))


def selected(item, source):
    if not item.get("description") or item.get("icon", 0) <= 0:
        return False
    if source == "weapons":
        return item["id"] % 10000 == 0  # Base records only: never guess affinity/upgrade joins.
    if source == "goods":
        return item["category"] in GOODS
    return source != "armor" or item["name"] in ARMOR


def image(path):
    data = path.read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WEBP":
        raise ValueError(f"Not a WebP image: {path}")
    with Image.open(io.BytesIO(data)) as check:
        check.verify()
    result = Image.open(io.BytesIO(data))
    if max(result.size) > 4096:
        raise ValueError(f"Oversized input: {path}")
    return result.convert("RGBA")


def webp(scratch, relative, picture, quality):
    target = scratch / "processed" / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    picture.save(target, "WEBP", quality=quality, method=6, exact=True)
    return target.read_bytes()


def attribution(modifications):
    return {"sourceUrl": SOURCE, "author": "FromSoftware / Bandai Namco; EthanShoeDev / elden-ring-compass contributors",
            "license": RIGHTS, "licenseUrl": SOURCE, "retrieved": "2026-10-29", "modifications": modifications}


def project(row, conversions):
    match = re.fullmatch(r"m(60|61)_(\d+)_(\d+)_\d([012])", row["mapId"])
    x, z = row["x"], row["z"]
    method = "overworld-grid"
    if match:
        size = 256 * 2 ** int(match[4])
        x += int(match[2]) * size + size / 2
        z += int(match[3]) * size + size / 2
        layer = "M00" if match[1] == "60" else "M10"
    else:
        points = conversions.get(row["mapId"][:9], [])
        if not points:
            return None
        nearest = min(points, key=lambda point: (x - point["srcX"]) ** 2 + (z - point["srcZ"]) ** 2)
        x += nearest["addX"]
        z += nearest["addZ"]
        layer = nearest["master"]
        method = "legacy-nearest-basepoint"
    x, y = round(x - 7168, 4), round(16640 - z, 4)
    if layer not in {"M00", "M01", "M10"} or not (0 <= x <= 10496 and 0 <= y <= 10496):
        return None
    return {"layer": layer, "x": str(x), "y": str(y), "projection": method}


def positions(scratch):
    conversions = defaultdict(list)
    for row in array(scratch, "world-map-legacy-conv"):
        conversions[row["srcMapId"]].append(row)

    entities = defaultdict(list)
    for row in array(scratch, "markers"):
        entities[row["entityId"]].append(row)
    candidates = []
    for row in array(scratch, "graces"):
        # Ambiguous entity IDs must not acquire a guessed location.
        matches = entities[row["bonfireEntityId"]]
        if len(matches) == 1:
            candidates.append((row["flagId"], row["name"], matches[0]))
    for row in array(scratch, "bosses"):
        candidates.append((row["defeatFlagId"], row["name"], row))
    grouped = defaultdict(list)
    for event, name, row in candidates:
        projected = project(row, conversions)
        if projected:
            grouped[event].append((name, projected))
    records = []
    for event, matches in sorted(grouped.items()):
        distinct = {json.dumps(position, sort_keys=True) for _, position in matches}
        if len(distinct) != 1:
            continue
        name, position = matches[0]
        records.append({"id": str(event), "name": name or f"Source event {event}", "attributes": position})
    return records


def validated_inputs(scratch):
    pins = json.loads((ROOT / "Evidence/visual-inputs.json").read_text(encoding="utf-8"))
    for entry in pins:
        if digest((scratch / entry["path"]).read_bytes()) != entry["sha256"]:
            raise ValueError(f"Source hash mismatch: {entry['path']}")
    return pins


def visual_manifest():
    manifest = json.loads((PACK / "pack.json").read_text(encoding="utf-8"))
    manifest["version"] = "3.0.0"
    manifest["title"] = "Elden Ring identities and selected game content (English)"
    manifest["description"] = "Complete pinned identity tables, selected exact-ID in-game text/icons and inferred regional map placement."
    manifest["datasets"] = manifest["datasets"][:1]
    names = manifest["datasets"][0]
    names["attribution"]["license"] = RIGHTS
    names["attribution"]["licenseUrl"] = SOURCE
    names["attribution"]["sourceUrl"] = SOURCE + "/packages/data/src/generated"
    names["attribution"]["author"] = "ClayAmore / ER-Save-Editor (MIT names); FromSoftware / Bandai Namco; EthanShoeDev / Compass (game content)"
    names["attribution"]["retrieved"] = "2026-10-29"
    names["attribution"]["modifications"] = ("ClayAmore names remain MIT (full notice adjacent). "
        + f"Selected exact ID AND unchanged-name joins to publisher descriptions in {SOURCE}/packages/data/src/generated; "
        + "goods 8600 has one reviewed punctuation alias (Limgrave- West / Limgrave, West); identities remain unchanged. "
        + "category + upstreamId identify the source row (weapon=weapons, goods=goods, armor=armor, talisman=talismans, ash=ashes-of-war). "
        + "Inferred PS5 compatibility; no gameplay limits or writes added. Icon assets have separate attribution.")
    return manifest


def enrich_names(scratch, names):
    images = {}
    icon_cache = {}
    counts = Counter()
    for file, source in TABLES.items():
        source_rows = {row["id"]: row for row in array(scratch, source)}
        target = PACK / (file + ".json")
        document = json.loads(target.read_text(encoding="utf-8"))
        for row in document["records"]:
            row.pop("assetId", None)
            row["description"] = UNCERTAINTY
            upstream = source_rows.get(int(row["attributes"]["upstreamId"]))
            if not upstream or not selected(upstream, source):
                continue
            if upstream["name"] != row["name"] and NAME_ALIASES.get((source, upstream["id"])) != (row["name"], upstream["name"]):
                continue
            icon = upstream["icon"]
            if icon not in icon_cache:
                picture = image(scratch / f"packages/data/images/icons/items/{icon}.webp")
                picture.thumbnail((64, 64), Image.Resampling.LANCZOS)
                data = webp(scratch, f"icons/{icon}.webp", picture, 65)
                # Content-addressed names also deduplicate different upstream IDs with identical output.
                path = "icons/" + digest(data)[:24] + ".webp"
                images[path] = data
                icon_cache[icon] = path
            row["assetId"] = icon_cache[icon]
            row["description"] = "\n".join(upstream["description"]) + "\n\n" + UNCERTAINTY
            counts[source] += 1
        save_json(target, document)
        next(entry for entry in names["files"] if entry["path"] == target.name)["sha256"] = digest(target.read_bytes())
    return images, counts


def add_map_images(scratch, pins, images):
    # Zoom 3 is 1/8 native resolution: preserve the entire 10496-square canvas, including blank margins.
    tiles = [entry["path"] for entry in pins if "/map-tiles/" in entry["path"] and "/base/3/" in entry["path"]]
    for layer in ["M00", "M01", "M10"]:
        canvas = Image.new("RGBA", (1312, 1312), (0, 0, 0, 0))
        for path in tiles:
            if f"/{layer}/" not in path:
                continue
            parts = pathlib.PurePosixPath(path).parts
            x, y = int(pathlib.PurePosixPath(path).stem), int(parts[-2])
            canvas.paste(image(scratch / path), (x * 256, y * 256))
        data = webp(scratch, f"maps/{layer}.webp", canvas, 55)
        if len(data) > 600_000:
            raise ValueError("Map exceeds size budget")
        images[f"maps/{layer}.webp"] = data


def add_asset_datasets(manifest, images, icon_paths):
    for title, paths in [("icons", icon_paths), ("maps", sorted(set(images) - set(icon_paths)))]:
        manifest["datasets"].append({"id": "eldenring-" + title, "kind": "assets", "title": "Selected publisher " + title,
            "files": [{"path": path, "sha256": digest(images[path])} for path in paths],
            "attribution": attribution("64px WebP icons; deduplicated." if title == "icons" else
                "Base-layer zoom-3 tiles stitched on a 1312-square canvas (1/8 native). Blank margins retained; WebP quality 55.")})


def add_position_dataset(scratch, manifest):
    coordinates = positions(scratch)
    save_json(PACK / "map-positions.json", {"records": coordinates})
    manifest["datasets"].append({"id": "eldenring-map-positions", "kind": "records", "title": "Inferred datamined map placements",
        "files": [{"path": "map-positions.json", "sha256": digest((PACK / "map-positions.json").read_bytes())}],
        "attribution": attribution("Exact event-ID joins; native 10496-square master pixels: x=worldX-7168, y=16640-worldZ. "
            "Legacy dungeons use the nearest WorldMapLegacyConvParam basepoint translation. Inferred calibration/legacy alignment; "
            "not console-verified. Ambiguous, missing, unsupported-layer and out-of-bounds joins omitted; statuses remain Unknown.")})
    return coordinates


def write_pack(manifest, images):
    for path, data in images.items():
        target = PACK / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    save_json(PACK / "pack.json", manifest)


def main():
    scratch = pathlib.Path(sys.argv[1]).resolve()
    pins = validated_inputs(scratch)
    manifest = visual_manifest()
    images, counts = enrich_names(scratch, manifest["datasets"][0])
    icon_paths = sorted(images)
    add_map_images(scratch, pins, images)
    total = sum(map(len, images.values()))
    if total > 4_000_000:
        raise ValueError(f"Asset budget exceeded: {total}")
    add_asset_datasets(manifest, images, icon_paths)
    coordinates = add_position_dataset(scratch, manifest)
    write_pack(manifest, images)
    summary = {"iconRecords": sum(counts.values()), "uniqueIcons": len(icon_paths), "descriptions": sum(counts.values()),
               "categories": dict(counts), "backgrounds": 3, "positions": len(coordinates), "assetBytes": total}
    save_json(ROOT / "Evidence/visual-summary.json", summary)
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
