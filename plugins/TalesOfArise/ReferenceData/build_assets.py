"""Rebuild the reviewed asset overlay, processing untrusted images outside the repository."""

import argparse
import hashlib
import io
import json
import urllib.request
from pathlib import Path

from PIL import Image


PLUGIN = Path(__file__).resolve().parents[1]
PACK = PLUGIN / "SaveToolkit.Plugins.TalesOfArise/ReferencePacks/talesofarise.catalog"
MEDIA_LICENSE = ("Rights reserved by Bandai Namco Entertainment; "
                 "used with attribution per owner decision 2026-10-29")
ASSET_BUDGET = 4_000_000
MAX_DOWNLOAD = 8_000_000
Image.MAX_IMAGE_PIXELS = 16_000_000


def digest(content):
    return hashlib.sha256(content).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n",
                    encoding="utf-8", newline="\n")


def download(asset, scratch):
    target = scratch / (digest(asset["sourceUrl"].encode()) + ".download")
    if not target.exists():
        request = urllib.request.Request(asset["sourceUrl"], headers={
            "User-Agent": "Mozilla/5.0", "Referer": "https://aselia.fandom.com/"})
        with urllib.request.urlopen(request, timeout=30) as response:
            content = response.read(MAX_DOWNLOAD + 1)
        if len(content) > MAX_DOWNLOAD:
            raise ValueError("Image exceeds download budget: " + asset["sourceUrl"])
        target.write_bytes(content)
    content = target.read_bytes()
    if digest(content) != asset["originalSha256"]:
        raise ValueError("Upstream image changed: " + asset["sourceUrl"])
    return content


def encode_image(content, is_map):
    # Pillow verifies the actual image header/decoder, rather than trusting a URL suffix.
    with Image.open(io.BytesIO(content)) as image:
        if image.format not in {"PNG", "JPEG", "WEBP"}:
            raise ValueError("Unsupported source image type: " + str(image.format))
        image.verify()
    with Image.open(io.BytesIO(content)) as image:
        image.load()
        image = image.convert("RGBA")
        edge = 2048 if is_map else 64
        image.thumbnail((edge, edge), Image.Resampling.LANCZOS)
        output = io.BytesIO()
        image.save(output, "WEBP", lossless=not is_map, quality=78 if is_map else 100,
                   method=6, exact=True)
        webp = output.getvalue()
        if is_map:
            if len(webp) > 600_000:
                raise ValueError("Map exceeds its budget")
            return webp, ".webp", image.size
        # PNG-8 can be substantially smaller for the game's simple category symbols.
        output = io.BytesIO()
        image.quantize(colors=256, method=Image.Quantize.FASTOCTREE).save(
            output, "PNG", optimize=True)
        png = output.getvalue()
        return (png, ".png", image.size) if len(png) < len(webp) else (webp, ".webp", image.size)


def encode_assets(ledger, scratch):
    assets, remap, unique = [], {}, {}
    for asset in ledger["assets"]:
        content, extension, dimensions = encode_image(download(asset, scratch), asset["role"] == "map")
        fingerprint = digest(content)
        path = unique.setdefault(fingerprint, str(Path(asset["path"]).with_suffix(extension)).replace("\\", "/"))
        if "sha256" in asset and (fingerprint != asset["sha256"] or path != asset["path"]):
            raise ValueError("Encoder output changed; review the Pillow/libwebp version before updating pins")
        remap[asset["path"]] = path
        # All encoding happens in scratch; copy only the validated, budgeted final bytes.
        temporary = scratch / Path(path).name
        temporary.write_bytes(content)
        assets.append({**asset, "path": path, "sha256": fingerprint,
                       "bytes": len(content), "width": dimensions[0], "height": dimensions[1]})
    total = sum(a["bytes"] for i, a in enumerate(assets) if a["path"] not in {x["path"] for x in assets[:i]})
    if total > ASSET_BUDGET:
        raise ValueError("Asset overlay exceeds four million bytes")
    return assets, remap, len(unique), total


def copy_assets(assets, scratch):
    for asset in assets:
        target = PACK / asset["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((scratch / target.name).read_bytes())


def apply_record_overlay(ledger, assets):
    for file in PACK.glob("*.json"):
        dataset = json.loads(file.read_text(encoding="utf-8"))
        if not isinstance(dataset, dict) or "records" not in dataset:
            continue
        for record in dataset["records"]:
            mapping = next((m for m in ledger["records"] if m["catalog"] == file.stem and m["id"] == record["id"]), None)
            if mapping:
                record["assetId"] = mapping["asset"]
                record["attributes"]["iconRole"] = mapping["iconRole"]
                record["attributes"]["iconSource"] = next(a["page"] for a in assets if a["path"] == mapping["asset"])
            description = next((d for d in ledger["descriptions"] if d["catalog"] == file.stem and d["id"] == record["id"]), None)
            if description:
                prefix = ("Official in-game description (Aselia transcription): " + description["text"] +
                          "\nDescription source: " + description["source"] +
                          "; Aselia Wiki contributors, CC BY-SA 3.0; publisher text: " + MEDIA_LICENSE + "\n")
                if not record["description"].startswith(prefix):
                    record["description"] = prefix + record["description"]
                record["attributes"]["descriptionSource"] = description["source"]
        write_json(file, dataset)


def write_manifest(assets, retrieved):
    manifest = json.loads((PACK / "pack.json").read_text(encoding="utf-8"))
    manifest["version"] = "3.0.0"
    manifest["datasets"] = [d for d in manifest["datasets"] if d["kind"] != "assets"]
    for dataset in manifest["datasets"]:
        modifications = dataset["attribution"]["modifications"]
        if "v3 adds separately" not in modifications:
            dataset["attribution"]["modifications"] = modifications.replace(
                "omitted images", "originally omitted images; v3 adds separately attributed publisher media and official transcriptions (assets.source.md)")
        for file in dataset["files"]:
            file["sha256"] = digest((PACK / file["path"]).read_bytes())
    for role, source in [("icon", "https://aselia.fandom.com/wiki/ToA_-_Skills?oldid=378099"),
                         ("map", "https://w.atwiki.jp/talesofdic/pages/18355.html")]:
        files = {a["path"]: {"path": a["path"], "sha256": a["sha256"]} for a in assets if a["role"] == role}
        manifest["datasets"].append({"id": "game-" + role + "s", "kind": "assets", "files": list(files.values()),
            "attribution": {"sourceUrl": source, "author": "Bandai Namco Entertainment Inc.; hosted by Aselia Wiki / Gamer Guides" if role == "icon" else "Bandai Namco Entertainment Inc.; hosted by Tales terminology dictionary (atwiki)",
            "license": MEDIA_LICENSE, "licenseUrl": "https://www.bandainamcoent.com/legal/terms",
            "retrieved": retrieved, "modifications": "Downscaled icons to at most 64x64; PNG-8 or lossless WebP, deduplicated. Map converted to WebP quality 78 without crop. Sources and hashes: asset-sources.json. Map is decorative behind a schematic; markers are not geographic."}})
    write_json(PACK / "pack.json", manifest)


def build(scratch):
    repository = PLUGIN.parents[1]
    scratch = scratch.resolve()
    if scratch == repository or repository in scratch.parents:
        raise ValueError("Image processing must stay outside the repository")
    scratch.mkdir(parents=True, exist_ok=True)
    ledger = json.loads((PACK / "asset-sources.json").read_text(encoding="utf-8"))
    assets, remap, image_count, total = encode_assets(ledger, scratch)
    copy_assets(assets, scratch)
    ledger["assets"] = assets
    for mapping in ledger["records"]:
        mapping["asset"] = remap[mapping["asset"]]
    apply_record_overlay(ledger, assets)
    write_json(PACK / "asset-sources.json", ledger)
    write_manifest(assets, ledger["retrieved"])
    print(f"Embedded artwork: {image_count} unique images, {total} bytes; {len(ledger['records'])} icon records; {len(ledger['descriptions'])} description records")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scratch", required=True, type=Path)
    build(parser.parse_args().scratch)
