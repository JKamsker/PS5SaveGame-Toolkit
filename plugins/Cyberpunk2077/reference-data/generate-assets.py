"""Build the attributed asset pack from reviewed inputs in an external scratch directory.

Run: python -I generate-assets.py <scratch-directory>. Requires Pillow. Downloaded
JavaScript is parsed as data, never executed. All image decoding/encoding happens
in scratch; only validated, bounded final images are copied into the plugin.
See the pack's data.source.md and upstream.json for retrieval URLs and input hashes.
"""
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import sys
from urllib.parse import quote
import zlib

from PIL import Image

PACK_ID = "cyberpunk2077.game-assets.en"
PLANNER = "https://www.cyberpunk.net/en/build-planner"
PLANNER_CHUNK = "https://www.cyberpunk.net/build/8603-b6e6f72c476c734a52a4.js"
MAP = "https://maps.piggyback.com/cyberpunk-2077/maps/night-city"
RETRIEVED = "2026-10-29"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n",
                    encoding="utf-8", newline="\n")


def field(text, name):
    match = re.search(r"\|" + name + r"[ \t]*=[ \t]*(.*?)(?=\n[ \t]*\||\n\}\})", text, re.S)
    return match.group(1).strip() if match else ""


def plain(text):
    text = re.sub(r"\[\[(?:[^\]|]+\|)?([^\]]+)\]\]", r"\1", text)
    text = re.sub(r"<br\s*/?>", "\n", text, flags=re.I)
    text = re.sub(r"</?\d+>", "", text)  # Planner React emphasis placeholders.
    if "{{" in text or "<" in text:
        raise ValueError("Unresolved markup: " + text)
    return html.unescape(text).strip()


def attribution(source, author, license_name, license_url, modifications):
    return dict(sourceUrl=source, author=author, license=license_name, licenseUrl=license_url,
                retrieved=RETRIEVED, modifications=modifications)


def main():
    scratch = Path(sys.argv[1]).resolve()
    output = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.Cyberpunk2077/ReferencePacks" / PACK_ID
    output.mkdir(parents=True, exist_ok=True)
    processed = scratch / "processed"
    (processed / "icons").mkdir(parents=True, exist_ok=True)
    (processed / "maps").mkdir(exist_ok=True)
    images = json.loads((scratch / "image-info.json").read_text(encoding="utf-8"))["query"]["pages"]
    image_info = {p["title"][5:]: p["imageinfo"][0] for p in images.values() if "imageinfo" in p}
    pages = json.loads((scratch / "item-pages.json").read_text(encoding="utf-8"))["query"]["pages"]
    records, sources, icons, seen = [], [], {}, {}
    for page in sorted(pages.values(), key=lambda p: p["title"]):
        if "revisions" not in page:
            continue
        revision = page["revisions"][0]
        text = revision["slots"]["main"]["*"]
        image_name = field(text, "image")
        bases = field(text, "baseid")
        if not image_name or not bases:
            continue
        source = "https://cyberpunk.fandom.com/wiki/" + quote(page["title"].replace(" ", "_")) + "?oldid=" + str(revision["revid"])
        image_path = scratch / image_name
        with Image.open(image_path) as image:
            if image.format != "PNG":
                raise ValueError("Unexpected icon type")
            image.verify()
        with Image.open(image_path) as image:
            image = image.convert("RGBA")
            image.thumbnail((64, 64), Image.Resampling.LANCZOS)
            candidate = processed / "icon.webp"
            image.save(candidate, format="WEBP", quality=82, method=6)
        image_hash = digest(candidate)
        asset = seen.get(image_hash)
        if asset is None:
            asset = "icons/" + image_hash[:16] + ".webp"
            shutil.copyfile(candidate, processed / asset)
            seen[image_hash] = asset
        icons[image_name] = asset
        description = plain(field(text, "caption"))
        if not description and page["title"] == "Smart Link":
            # Exact first sentence of the game's effect text, without tier-dependent stats.
            description = plain(field(text, "effects").split("\n")[0])
        cyberware = "{{Infobox Cyberware" in text
        for base in re.split(r"<br\s*/?>|\n", bases, flags=re.I):
            if not re.fullmatch(r"[A-Za-z0-9_]+", base.strip()):
                raise ValueError("Unexpected explicit base ID: " + base)
            internal = "Items." + base.strip()
            raw = internal.encode("utf-8")
            key = str(zlib.crc32(raw) | (len(raw) << 32))
            records.append(dict(id=key, name=page["title"], category="Cyberware" if cyberware else "Item",
                                description=description, assetId=asset, source=source,
                                attributes=dict(internalName=internal)))
        info = image_info[image_name]
        sources.append(dict(title=page["title"], sourceUrl=source, wikitextSha256=hashlib.sha256(text.encode()).hexdigest(),
                            imageUrl=info["url"] + "&format=original", imageSha256=digest(image_path),
                            imageSha1=info["sha1"], asset=asset))
    if len({r["id"] for r in records}) != len(records):
        raise ValueError("Duplicate item identities")
    write(output / "item-art.json", dict(records=sorted(records, key=lambda r: int(r["id"]))))
    # Unescape only the JSON string wrapper; no JS evaluation or downloaded code execution.
    script = (scratch / "perks.js").read_text(encoding="utf-8")
    literal = re.fullmatch(r'.*JSON.parse\(\'(.*)\'\).*', script, re.S).group(1)
    literal = literal.replace("\\'", "'").replace("\\\\", "\x00").replace('\\"', '"').replace("\x00", "\\")
    planner = json.loads(literal)
    perks = []
    for key, name in planner.items():
        if not key.startswith("Perk-Name-"):
            continue
        identifier = key.removeprefix("Perk-Name-")
        perks.append(dict(id=identifier, name=name, category=identifier.split("_")[0],
                          description=plain(planner["Perk-Description-" + identifier]), source=PLANNER))
    write(output / "perks.json", dict(records=perks))
    # Public zoom-3 tiles use top-left origin, 256px cells, x/y 0..6.
    # No save-ID/position crosswalk is implied by this image-space assembly.
    map_image = Image.new("RGB", (1792, 1792))
    tile_sources = []
    for x in range(7):
        for y in range(7):
            tile_path = scratch / f"tile-{x}-{y}.webp"
            with Image.open(tile_path) as tile:
                if tile.format != "WEBP" or tile.size != (256, 256):
                    raise ValueError("Unexpected tile header")
                tile.verify()
            with Image.open(tile_path) as tile:
                map_image.paste(tile, (x * 256, y * 256))
            tile_sources.append(dict(url=f"https://storage-cdn.piggyback.com/images/tiles/cbp/3/{x}/{y}.webp",
                                     sha256=digest(tile_path)))
    map_image.save(processed / "maps/night-city.webp", quality=72, method=6)
    if (processed / "maps/night-city.webp").stat().st_size > 600_000:
        raise ValueError("Map exceeds its budget")
    assets = sorted({*icons.values(), "maps/night-city.webp"})
    for asset in assets:
        destination = output / asset
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(processed / asset, destination)
    reserved = "Rights reserved by {}; used with attribution per owner decision 2026-10-29"
    text_attr = attribution("https://cyberpunk.fandom.com/wiki/Cyberpunk_Wiki", "Cyberpunk Wiki contributors / CD PROJEKT RED",
                            "CC-BY-SA-3.0", "https://creativecommons.org/licenses/by-sa/3.0/",
                            "Explicit base IDs, titles and in-game infobox captions; wiki markup removed. Build/tier effects unverified; no limits.")
    art_attr = attribution("https://cyberpunk.fandom.com/wiki/Category:Cyberpunk_2077_images_-_Inventory_Icons", "CD PROJEKT RED / Cyberpunk Wiki uploaders",
                           reserved.format("CD PROJEKT RED"), "https://www.cyberpunk.net/en/", "Selected inventory icons downscaled to 64px WebP and content-deduplicated; source ledger pins originals.")
    planner_attr = attribution(PLANNER, "CD PROJEKT RED", reserved.format("CD PROJEKT RED"), PLANNER,
                               "English perk names and descriptions parsed from content-hashed official localization chunk; emphasis markup removed. Reference text only, not PS5 2.21 rules.")
    map_attr = attribution(MAP, "Piggyback Interactive Limited / CD PROJEKT RED", reserved.format("Piggyback / CDPR"),
                           "https://www.piggyback.com/us/terms-of-use/", "49 public zoom-3 tiles assembled at their native tile indices; 1792px WebP quality 72. No verified saved travel-ID crosswalk; markers remain in text list.")
    datasets = []
    for catalog, attr in [("item-art", text_attr), ("perks", planner_attr)]:
        datasets.append(dict(id=catalog, kind="records", files=[dict(path=catalog + ".json", sha256=digest(output / (catalog + ".json")))], attribution=attr))
    for name, paths, attr in [("icons", sorted(set(icons.values())), art_attr), ("map", ["maps/night-city.webp"], map_attr)]:
        datasets.append(dict(id=name, kind="assets", files=[dict(path=p, sha256=digest(output / p)) for p in paths], attribution=attr))
    write(output / "pack.json", dict(schema=1, id=PACK_ID, version="1.0.0", title="Cyberpunk 2077 attributed game art and reference text",
                                      plugins=["cyberpunk2077"], titleIds=["PPSA04027"], gameBuilds=[], locale="en", datasets=datasets))
    write(output / "upstream.json", dict(retrieved=RETRIEVED, itemPages=sources, planner=dict(url=PLANNER_CHUNK, sha256=digest(scratch / "perks.js")),
                                         map=dict(sourceUrl=MAP, tiles=tile_sources), inputHashes={p: digest(scratch / p) for p in ["item-pages.json", "image-info.json"]}))
    print(f"{len(set(icons.values()))} icons; {len(records)} item descriptions; {len(perks)} perk descriptions; 1 map; {sum((output / p).stat().st_size for p in assets)} image bytes")


if __name__ == "__main__":
    main()
