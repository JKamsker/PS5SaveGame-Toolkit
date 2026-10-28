"""Rebuild published identifier packs offline; never executes downloaded code.

Run with python -I plugins/FinalFantasy16/research/build_published_catalog.py.
Reviewed upstream CSV bytes are retained beside their attribution sidecar.
"""
import csv
import hashlib
import io
import json
from collections import defaultdict
from pathlib import Path
import re

PLUGIN = Path(__file__).resolve().parents[1]
PACK = PLUGIN / "SaveToolkit.Plugins.FinalFantasy16/ReferencePacks/ffxvi.published.en"
SOURCES = PLUGIN / "research/published-ids"
REVISION = "4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03"
ROOT_URL = f"https://github.com/Nenkai/ffxvi-modding/blob/{REVISION}/docs/resources/ids"
LICENSE = "No licence published; reused with attribution per owner decision 2026-10-28"
HASHES = {
    "item_ids.csv": "90a39daa4656115ac79fd354ae1593634962dc0171b2bef94e568f6e407f60c1",
    "equip_item_ids.csv": "fba2cff3e6ac4781a26cda158456aad174a17d2fb3ea1bcfd52830ce94579355",
    "gamemap_ids.csv": "b7a5d48c040427703437d546b866c7b0a1aa71d86ff7b892c7dc6cd36cea627a",
    "bnpc_ids.csv": "009b194bf58b1333fe992b4b28aca4fe1ccd84dd2f8b32e36d3b69f1a0b73a1b",
    "enpc_ids.csv": "54d6c50d2cb773d1bd13bfb74b633e90b29ee43bf5513823e18460f52e897bc4",
}
WARNING = ("Published community identifier; PS5 mapping confidence: Inferred. "
           "Catalogue presence does not prove ownership, availability, active state or gameplay limits. "
           "No save-index, quest-state or NPC-instance association is established.")


def write_json(path, value):
    content = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    path.write_bytes(content)
    return hashlib.sha256(content).hexdigest()


def read_csv(name):
    data = (SOURCES / name).read_bytes()
    # Git can convert line endings; hashes use LF, matching F2 records hashing.
    if hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest() != HASHES[name]:
        raise ValueError(f"Pinned source hash mismatch: {name}")
    return list(csv.DictReader(io.StringIO(data.decode("utf-8-sig"))))


def plain_text(name):
    return re.sub(r"</?i>|<color=[a-zA-Z0-9_]+>|</color>", "", name).strip()


def item_category(key):
    # Original inferred taxonomy from the published ID bands, not a gameplay rule.
    if 100001 <= key < 100200:
        return "weapon"
    if 100201 <= key < 100300:
        return "belt"
    if 100301 <= key < 100400:
        return "vambrace"
    if 100401 <= key <= 100622:
        return "accessory"
    if key in (1, 2, 3):
        return "resource"
    if 5 <= key <= 11:
        return "consumable"
    if 21 <= key < 500 or 1001 <= key < 1200:
        return "material"
    if 501 <= key < 600 or 705 <= key < 800:
        return "curiosity"
    if 604 <= key < 700:
        return "orchestrion"
    if 1203 <= key <= 1352:
        return "quest-item"
    raise ValueError(f"Unreviewed category: {key}")


def records_for(name, category=None):
    groups = defaultdict(list)
    rows = read_csv(name)
    for row in rows:
        key = row.get("Key", row.get("Id"))
        if str(int(key)) != key or int(key) < 0:
            raise ValueError(f"Invalid identifier in {name}: {key}")
        if row["Name"].strip() not in ("", "**(no name)**"):
            groups[key].append(row)
    records = []
    for key, variants in sorted(groups.items(), key=lambda pair: int(pair[0])):
        if len(variants) != 1 and name != "gamemap_ids.csv":
            raise ValueError(f"Duplicate identifier in {name}: {key}")
        names = list(dict.fromkeys(plain_text(row["Name"]) for row in variants))
        attributes = {"confidence": "Inferred", "identifierSource": f"{ROOT_URL}/{name}",
                      "sourceLicense": LICENSE, "categoryConfidence": "Inferred"}
        if name == "gamemap_ids.csv":
            attributes["variantCount"] = str(len(variants))
            for index, row in enumerate(variants):
                attributes[f"variant_{index}"] = " | ".join(filter(None, [
                    plain_text(row["Name"]), row["MapFile"].strip(), row["Note"].strip()]))
        elif variants[0]["Name"] != names[0]:
            attributes["originalName"] = variants[0]["Name"]
        if len(attributes) > 32:
            raise ValueError(f"Too many variant attributes: {key}")
        records.append({"id": key, "name": " / ".join(names),
                        "category": category or item_category(int(key)),
                        "source": f"{ROOT_URL}/{name}", "description": WARNING,
                        "attributes": attributes})
    return records, {"rows": len(rows), "namedRows": sum(map(len, groups.values())),
                     "namedIds": len(groups), "omittedUnnamedRows": len(rows) - sum(map(len, groups.values()))}


def main():
    PACK.mkdir(parents=True, exist_ok=True)
    items, item_counts = records_for("item_ids.csv")
    equipment, equipment_counts = records_for("equip_item_ids.csv")
    maps, map_counts = records_for("gamemap_ids.csv", "map-reference")
    bnpcs, bnpc_counts = records_for("bnpc_ids.csv", "battle-npc-reference")
    enpcs, enpc_counts = records_for("enpc_ids.csv", "event-npc-reference")
    source_counts = {"item_ids.csv": item_counts, "equip_item_ids.csv": equipment_counts,
                     "gamemap_ids.csv": map_counts, "bnpc_ids.csv": bnpc_counts, "enpc_ids.csv": enpc_counts}
    catalogs = [
        ("ffxvi_published_items", items + equipment, ["item_ids.csv", "equip_item_ids.csv"]),
        ("ffxvi_published_maps", maps, ["gamemap_ids.csv"]),
        ("ffxvi_published_bnpc", bnpcs, ["bnpc_ids.csv"]),
        ("ffxvi_published_enpc", enpcs, ["enpc_ids.csv"]),
    ]
    datasets = []
    for catalog, records, files in catalogs:
        if len({record["id"] for record in records}) != len(records):
            raise ValueError(f"Duplicate ID in {catalog}")
        filename = catalog + ".json"
        digest = write_json(PACK / filename, {"records": records})
        datasets.append({"id": catalog, "kind": "records", "title": catalog.replace("_", " "),
                         "files": [{"path": filename, "sha256": digest}], "attribution": {
                             "sourceUrl": f"{ROOT_URL}/{files[0]}",
                             "author": "Nenkai and ffxvi-modding contributors", "license": LICENSE,
                             "licenseUrl": f"https://github.com/Nenkai/ffxvi-modding/tree/{REVISION}",
                             "retrieved": "2026-10-29",
                             "modifications": "Imported published identifier/name facts from " + ", ".join(files) +
                             "; omitted unnamed placeholders, removed documented display tags, grouped map variants, "
                             "added original inferred categories and mapping warnings. No art, prose or game assets."}})
    write_json(PACK / "pack.json", {
        "schema": 1, "id": "ffxvi.published.en", "version": "1.0.0",
        "title": "FFXVI published community identifiers (Inferred)", "plugins": ["ffxvi"],
        "titleIds": ["PPSA10665", "PPSA13803"], "gameBuilds": [], "locale": "en",
        "description": "Published names only; unknown game-build coverage, no gameplay caps or save-index joins.",
        "datasets": datasets})
    old_audit = json.loads((PLUGIN / "research/round2-naming-audit.json").read_bytes())
    published = {int(record["id"]): record for record in items + equipment}
    resolved = [{"id": row["id"], "name": published[row["id"]]["name"], "confidence": "Inferred",
                 "source": published[row["id"]]["source"]} for row in old_audit["items"] if row["status"] == "Unknown"]
    write_json(PLUGIN / "research/unlicensed-naming-audit.json", {
        "format": "ffxvi-unlicensed-naming-audit/1", "reviewedOn": "2026-10-29", "revision": REVISION,
        "license": LICENSE, "supersedes": "round2-naming-audit.json: 41 remaining unnamed IDs",
        "sources": {name: {"sha256Lf": HASHES[name], **counts} for name, counts in source_counts.items()},
        "catalogCounts": {catalog: len(records) for catalog, records, _ in catalogs},
        "newlyNamedObservedIds": resolved,
        "labelsAreNotGameplayEvidence": True})
    print("Published catalogues:", {catalog: len(records) for catalog, records, _ in catalogs})
    print(f"Resolved {len(resolved)} previously unnamed observed IDs; sample coverage must be checked separately.")


if __name__ == "__main__":
    main()
