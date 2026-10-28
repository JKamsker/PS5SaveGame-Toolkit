"""Regenerate factual F2 packs from inspected, pinned downloads (never execute them).

Usage: python -I import_community_tables.py <scratch-directory>
Expected downloads: items.zip, gow.CT, gow2018_items.json. See tables.source.md.
"""

import hashlib
import html
import json
import re
import sys
import zipfile
from pathlib import Path


LICENSE = "No licence published; reused with attribution per owner decision 2026-10-28"
RUNTIME_ROOT = "https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/"
OLD_ROOT = "https://github.com/xcier/GoW-2018-PS4-editor/blob/9342d5f3967ef74bac1161581dc4eab8bb41760d/"
PACKS = Path(__file__).resolve().parents[1] / "SaveToolkit.Plugins.GodOfWarRagnarok/ReferencePacks"
EXPECTED = {
    "items.zip": "a3c1f0a9737dce922b578f4108db5f861e74bb9d90919a75923c833bd5fb78bd",
    "gow.CT": "365a53fd4e47c70dc612f9583a5893d3f8d2d6deca2e128cdddf32d74294236d",
    "gow2018_items.json": "ce7b3e4aab1a72a6ad9b7365946a6072327b6c2d2c7cb1591956aa31e07e7213",
}


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def runtime_records(archive):
    with zipfile.ZipFile(archive) as source:
        document = source.read("Tabellenblatt1.html").decode("utf-8")
    records = {}
    for row in re.findall(r"<tr\b[^>]*>(.*?)</tr>", document, re.S):
        cells = [html.unescape(re.sub("<[^>]+>", "", cell)).strip()
                 for cell in re.findall(r"<td\b[^>]*>(.*?)</td>", row, re.S)]
        for index, cell in enumerate(cells):
            if not re.fullmatch("[0-9A-Fa-f]{8,16}", cell):
                continue
            key = int(cell, 16).to_bytes(8, "little").hex().upper()
            name, note = cells[index + 1:index + 3]
            known = name not in ("", "-", "Unnamed", "(CUT)", "Unique Resources")
            category = "XP" if name.endswith(" XP") else {
                "Currency": "currency", "Crafting Component": "crafting resource",
                "Resources": "resource", "Key Item": "key item", "Key Items": "key item",
                "Weapon Upgrades": "upgrade resource", "Unique Resource": "unique resource",
                "Artifacts": "artifact", "Crafting DLC": "Valhalla resource",
            }.get(note, "runtime candidate" if known else "Unknown")
            record = {
                "id": key, "name": name if known else f"Unknown runtime ID 0x{int(cell, 16):016X}",
                "category": category, "limit": None, "source": RUNTIME_ROOT + "item%23.zip",
                "description": "PC runtime table candidate; PS5 name/role Inferred when the exact ID occurs. "
                               "Ownership, progression effects, legal limits and console acceptance are unverified.",
                "attributes": {"sourceName": name, "sourceNote": note, "confidence": "Inferred" if known else "Unknown"},
            }
            if key in records and records[key] != record:
                raise ValueError(f"Conflicting runtime rows for {key}")
            records[key] = record
            break
    assert len(records) == 961
    return list(records.values())


def group_records(table):
    text = table.read_text(encoding="utf-8-sig")
    records = []
    for variable, name in [("Axe", "Leviathan Axe"), ("Blade", "Blades of Chaos"),
                           ("Spear", "Draupnir Spear"), ("Atreus", "Atreus")]:
        value = int(re.search(rf"local {variable} = 0x([0-9A-Fa-f]+)", text).group(1), 16)
        records.append({
            "id": value.to_bytes(8, "little").hex().upper(), "name": name + " skill tree (inferred)",
            "category": "skill tree", "source": RUNTIME_ROOT + "gow.CT", "limit": None,
            "description": "PC GetSkillTree argument matches a PS5 grouped-flag identity. "
                           "Individual skills and saved flag-bit meanings remain Unknown; no unlock writer is provided.",
        })
    return records


def old_records(database):
    records = []
    for item in json.loads(database.read_text(encoding="utf-8"))["items"]:
        records.append({
            "id": item["id"], "name": "2018 candidate: " + item["name"],
            "category": "2018 / " + item["type"], "source": OLD_ROOT + "app/resources/database/gow2018_items.json",
            "limit": None,
            "description": "God of War (2018) community crosswalk, not a verified Ragnarök identity. "
                           "A matching hash alone does not establish gear, quest, ownership or completion semantics.",
            "attributes": {"internalName": item["file"] or "Unknown", "sourceName": item["name"],
                           "sourceType": item["type"], "confidence": "Unknown", "game": "God of War (2018)"},
        })
    assert len(records) == 1682 and len({record["id"] for record in records}) == len(records)
    return records


def pack(pack_id, title, datasets):
    folder = PACKS / pack_id
    folder.mkdir(parents=True, exist_ok=True)
    entries = []
    for catalog, records, source, author in datasets:
        filename = catalog + ".json"
        write_json(folder / filename, {"records": records})
        entries.append({
            "id": catalog, "kind": "records", "title": catalog,
            "files": [{"path": filename, "sha256": hashlib.sha256((folder / filename).read_bytes()).hexdigest()}],
            "attribution": {"sourceUrl": source, "author": author, "license": LICENSE,
                            "licenseUrl": source, "retrieved": "2026-10-28",
                            "modifications": "Extracted factual IDs/names/categories only; normalized literal LE keys, "
                                             "deduplicated identical rows; no code, game prose, assets or legal maxima imported. "
                                             "Cross-game and PC-to-PS5 meanings retain explicit uncertainty."},
        })
    write_json(folder / "pack.json", {
        "schema": 1, "id": pack_id, "version": "1.0.0", "title": title,
        "plugins": ["gow-ragnarok"], "titleIds": ["PPSA08330"],
        "gameBuilds": ["PS5 revision 0x00513040"], "locale": "en", "datasets": entries,
    })


def main():
    scratch = Path(sys.argv[1])
    for name, expected in EXPECTED.items():
        if hashlib.sha256((scratch / name).read_bytes()).hexdigest() != expected:
            raise ValueError(f"Pinned source hash mismatch: {name}")
    pack("gow-ragnarok.runtime.en", "PC runtime identities (PS5 transfer inferred)", [
        ("gow-ragnarok-runtime", runtime_records(scratch / "items.zip"), RUNTIME_ROOT + "item%23.zip", "mooofin / community spreadsheet contributors"),
        ("gow-ragnarok-trees", group_records(scratch / "gow.CT"), RUNTIME_ROOT + "gow.CT", "mooofin"),
    ])
    pack("gow-ragnarok.2018.en", "2018 crosswalk candidates (Ragnarök semantics Unknown)", [
        ("gow-ragnarok-2018", old_records(scratch / "gow2018_items.json"),
         OLD_ROOT + "app/resources/database/gow2018_items.json", "ProtoBuffers / xcier / God of War IDs spreadsheet contributors"),
    ])


if __name__ == "__main__":
    main()
