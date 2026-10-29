"""Rebuild compact attributed references from reviewed images outside the repository.

Run with python -I. Inputs are the SHA-pinned 64px browser PNG captures and map
snapshot described in visual-reference.source.json. No network or upstream code.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import shutil

from PIL import Image

PACK_ID = "pragmata.evidence.en"
WIKI_LICENSE = "CC-BY-SA-3.0"
ASSET_LICENSE = "Rights reserved by Capcom; used with attribution per owner decision 2026-10-29"
MAP_LICENSE = "Rights reserved by Capcom and Itemaps; used with attribution per owner decision 2026-10-29"
WIKI_URL = "https://pragmata.fandom.com/wiki/Item?oldid=1830"
MAP_URL = "https://itemaps.com/maps/pragmata"
MAX_ASSET_BYTES = 4_000_000
MAP_SIZE = (2048, 585)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def checked_input(directory, filename, expected):
    raw = (directory / filename).read_bytes()
    if digest(raw) != expected:
        raise ValueError(f"Input differs from reviewed snapshot: {filename}")
    return raw


def write_json(path, data):
    raw = (json.dumps(data, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    path.write_bytes(raw)
    return {"path": path.name, "sha256": digest(raw)}


def attribution(source, author, license_name, license_url, modifications):
    return {"sourceUrl": source, "author": author, "license": license_name,
            "licenseUrl": license_url, "retrieved": "2026-10-29", "modifications": modifications}


def image_bytes(raw, expected_format, expected_size):
    with Image.open(io.BytesIO(raw)) as image:
        if image.format != expected_format or image.size != expected_size:
            raise ValueError("Unexpected image header or dimensions")
        image.verify()
    with Image.open(io.BytesIO(raw)) as image:
        return image.convert("RGBA")


def build(scratch, pack):
    metadata = json.loads((pack / "visual-reference.source.json").read_text(encoding="utf-8"))
    generated = scratch / "processed"
    generated.mkdir(exist_ok=True)
    icons = []
    records = []
    seen = {}
    for source in metadata["records"]:
        raw = checked_input(scratch, source["inputFile"], source["thumbnailSha256"])
        image = image_bytes(raw, "PNG", (64, 64))
        webp = io.BytesIO()
        image.save(webp, "WEBP", quality=85, method=6)
        optimized = webp.getvalue()
        # Reuse identical encoded images; no redundant assets in the pack.
        key = digest(optimized)
        if key not in seen:
            path = f"icons/{source['slug']}.webp"
            seen[key] = path
            (generated / Path(path).name).write_bytes(optimized)
            icons.append({"path": path, "sha256": key})
        records.append({"id": source["id"], "name": source["name"], "category": source["category"],
                        "description": source["description"], "assetId": seen[key], "source": source["source"],
                        "attributes": {"descriptionKind": source["descriptionKind"], "imageSource": source["filePage"]}})

    map_source = metadata["map"]
    raw = checked_input(scratch, map_source["inputFile"], map_source["sha256"])
    image = image_bytes(raw, "WEBP", (7168, 2048)).convert("RGB")
    image = image.resize(MAP_SIZE, Image.Resampling.LANCZOS)
    image.save(generated / "cradle.webp", "WEBP", quality=85, method=6)
    map_raw = (generated / "cradle.webp").read_bytes()
    if len(map_raw) > 600_000:
        raise ValueError("Map exceeds the size budget")
    markers = json.loads(checked_input(scratch, map_source["markersFile"], map_source["markersSha256"]))
    if markers["origin"] != "top-left" or (markers["image"]["width"], markers["image"]["height"]) != (7168, 2048):
        raise ValueError("Coordinate space changed")
    locations = []
    for marker in markers["markers"]:
        if marker["cat"] != "escape-hatch" or marker["plate"] != "the-cradle":
            continue
        x, y = marker["x"], marker["y"]
        if not (0 <= x <= 7168 and 0 <= y <= 2048):
            raise ValueError("Marker outside the source image")
        region = next((f["value"] for f in marker.get("facts", []) if f["label"] == "Region"), "Unknown region")
        locations.append({"id": marker["id"], "name": marker["name"], "category": region,
                          "attributes": {"x": str(x), "y": str(y)}})
    if len(locations) != 26:
        raise ValueError("Reviewed escape-hatch subset changed")
    binary_bytes = sum((generated / Path(f["path"]).name).stat().st_size for f in icons) + len(map_raw)
    if binary_bytes > MAX_ASSET_BYTES:
        raise ValueError("Assets exceed the plugin budget")
    # Image processing is complete before copying the compact final files into the pack.
    (pack / "icons").mkdir(exist_ok=True)
    (pack / "maps").mkdir(exist_ok=True)
    for icon in icons:
        shutil.copyfile(generated / Path(icon["path"]).name, pack / icon["path"])
    shutil.copyfile(generated / "cradle.webp", pack / "maps/cradle.webp")
    record_file = write_json(pack / "pragmata-illustrated-reference.json", {"records": records})
    location_file = write_json(pack / "pragmata-map-locations.json", {"records": locations})
    manifest = json.loads((pack / "pack.json").read_text(encoding="utf-8"))
    manifest["version"] = "1.5.0"
    new_ids = {"pragmata-illustrated-reference", "pragmata-map-locations", "item-icons", "map-art"}
    manifest["datasets"] = [d for d in manifest["datasets"] if d["id"] not in new_ids]
    wiki_attribution = attribution(WIKI_URL, "Pragmata Wiki contributors; Capcom (quoted game text)",
        WIKI_LICENSE + "; " + ASSET_LICENSE, "https://creativecommons.org/licenses/by-sa/3.0/",
        "Selected nine short official-description excerpts and six wiki summaries. Other descriptions unavailable. Wiki file IDs are reference keys, never PS5 item IDs. Revision and image ledger in visual-reference.source.json. CC BY-SA adaptations retain that licence.")
    icon_attribution = attribution(WIKI_URL, "Capcom; Pragmata Wiki uploaders (see file histories in source ledger)",
        ASSET_LICENSE, WIKI_URL,
        "29 item/weapon images; browser-decoded, alpha-cropped and resized to transparent 64x64 PNG outside repository, then WebP quality 85. Pixel fingerprints, crops and PNG hashes in source ledger. No save-ID associations inferred.")
    map_attribution = attribution(MAP_URL, "Capcom (game map art); Itemaps / Kevin Henrique (atlas and coordinates)",
        MAP_LICENSE, MAP_URL,
        "Retrieved snapshot pinned by SHA-256. Map resized from 7168x2048 to 2048x585 WebP quality 85. Selected 26 escape hatches; original top-left pixels transform by x*2048/7168, y*585/2048. No save-hash joins or completion states inferred.")
    for catalog, title, file, notice in (("pragmata-illustrated-reference", "Illustrated reference library (save IDs Unknown)", record_file, wiki_attribution),
                                        ("pragmata-map-locations", "Community escape-hatch locations", location_file, map_attribution)):
        manifest["datasets"].append({"id": catalog, "title": title, "kind": "records", "files": [file], "attribution": notice})
    manifest["datasets"].append({"id": "item-icons", "kind": "assets", "files": icons, "attribution": icon_attribution})
    manifest["datasets"].append({"id": "map-art", "kind": "assets", "files": [{"path": "maps/cradle.webp", "sha256": digest(map_raw)}], "attribution": map_attribution})
    (pack / "pack.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"icons": len(icons), "descriptions": 15, "mapBackgrounds": 1, "mapMarkers": len(locations),
                      "binaryAssetBytes": binary_bytes, "mapBytes": len(map_raw)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scratch", type=Path, help="Outside-repository folder containing the reviewed inputs")
    args = parser.parse_args()
    plugin = Path(__file__).resolve().parents[1]
    if plugin.parent.parent.resolve() in args.scratch.resolve().parents:
        parser.error("Image processing must take place outside the repository")
    build(args.scratch, plugin / "SaveToolkit.Plugins.Pragmata/ReferencePacks" / PACK_ID)
